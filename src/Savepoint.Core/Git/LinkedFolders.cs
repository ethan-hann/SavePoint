using System.Collections.Concurrent;
using System.IO.Enumeration;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Git;

// Git for Windows follows junctions and folder symbolic links as if they were ordinary folders, so a switch or a discard
// could overwrite or delete files outside the game folder through one. On Windows every operation looks at the folders
// that hold the files it is about to read, write or remove, and refuses before touching anything. Elsewhere git stores a
// link as a link and never goes through it. A file link is refused too: git for Windows stores it as the text of its target and
// writes that text back as an ordinary file, which would replace the link (a mod manager's link deployment).
internal static class LinkedFolders
{
    // Only Windows refuses links; elsewhere git stores a link as a link and never follows it.
    public static bool Enabled => OperatingSystem.IsWindows();

    // The folders (relative, '/'-separated, outermost first) that hold the given files; the game folder itself is not one.
    public static IReadOnlyList<string> FoldersOf(IEnumerable<string> files)
    {
        var folders = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            for (var slash = file.IndexOf('/', StringComparison.Ordinal); slash >= 0; slash = file.IndexOf('/', slash + 1))
            {
                folders.Add(file[..slash]);
            }
        }

        return [.. folders.Order(StringComparer.Ordinal)];
    }

    // Throws for the first linked folder (in path order) that holds one of the files, else for the first of the files that is itself a
    // link; nothing is read or written.
    public static void RefuseAny(string workTree, IEnumerable<string> files, CancellationToken ct)
    {
        if (!Enabled)
        {
            return;
        }

        var listed = files as IReadOnlyCollection<string> ?? files.ToList();
        var folders = FoldersOf(listed);
        var linked = new bool[folders.Count];
        Parallel.For(
            0,
            folders.Count,
            new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Environment.ProcessorCount },
            i => linked[i] = IsLink(Path.Combine(workTree, folders[i].Replace('/', Path.DirectorySeparatorChar))));

        for (var i = 0; i < folders.Count; i++)
        {
            if (linked[i])
            {
                throw new LinkedFolderException(Path.Combine(workTree, folders[i].Replace('/', Path.DirectorySeparatorChar)));
            }
        }

        RefuseLinkedFiles(workTree, listed, folders, ct);
    }

    // Git for Windows stores a file link as the text of its target and writes that text back as an ordinary file, so a mod manager's link
    // deployment would stop working after a switch. Each folder is listed once and the attributes that come with the listing say which
    // entries are reparse points, so no file is opened or asked about on its own (a folder of 100,000 files takes tens of milliseconds, where
    // a query per file takes seconds). Only entries among the given files count: one the ignore list names is left alone.
    private static void RefuseLinkedFiles(string workTree, IReadOnlyCollection<string> files, IReadOnlyList<string> folders, CancellationToken ct)
    {
        // Windows drives ignore case, so a link named A.ESP is in the way of a file the switch spells a.esp.
        var listed = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
        var found = new ConcurrentBag<(string Relative, bool IsFolder)>();
        var options = new EnumerationOptions { AttributesToSkip = 0, RecurseSubdirectories = false, IgnoreInaccessible = true };

        Parallel.ForEach(
            folders.Prepend(string.Empty),
            new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Environment.ProcessorCount },
            folder =>
            {
                var directory = folder.Length == 0 ? workTree : Path.Combine(workTree, folder.Replace('/', Path.DirectorySeparatorChar));
                // The listed files include recorded ones that are gone, and a switch writes into folders that are not there yet.
                try
                {
                    var entries = new FileSystemEnumerable<(string Name, bool IsFolder)>(directory, (ref FileSystemEntry entry) => (entry.FileName.ToString(), entry.IsDirectory), options)
                    {
                        ShouldIncludePredicate = (ref FileSystemEntry entry) => (entry.Attributes & FileAttributes.ReparsePoint) != 0,
                    };

                    foreach (var (name, isFolder) in entries)
                    {
                        var relative = folder.Length == 0 ? name : folder + "/" + name;
                        if (listed.Contains(relative) && LinkTargetOf(Path.Combine(directory, name), isFolder) is not null)
                        {
                            found.Add((relative, isFolder));
                        }
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // A folder that is missing or cannot be listed holds no link the operation could go through.
                }
            });

        if (found.IsEmpty)
        {
            return;
        }

        var first = found.OrderBy(entry => entry.Relative, StringComparer.Ordinal).First();
        var path = Path.Combine(workTree, first.Relative.Replace('/', Path.DirectorySeparatorChar));
        throw first.IsFolder ? new LinkedFolderException(path) : new LinkedFileException(path);
    }

    // Placeholders that only fetch content on demand are reparse points that lead nowhere else; only a real link counts.
    private static string? LinkTargetOf(string path, bool isFolder)
    {
        try
        {
            return isFolder ? new DirectoryInfo(path).LinkTarget : new FileInfo(path).LinkTarget;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // True when any folder between the game folder and the file is a link (on every system: the engine's own deletes use it).
    public static bool IsInsideLink(string workTree, string relativePath)
    {
        var slash = relativePath.IndexOf('/', StringComparison.Ordinal);
        while (slash >= 0)
        {
            if (IsLink(Path.Combine(workTree, relativePath[..slash].Replace('/', Path.DirectorySeparatorChar))))
            {
                return true;
            }

            slash = relativePath.IndexOf('/', slash + 1);
        }

        return false;
    }

    // A reparse point that leads somewhere is a junction or a link; placeholders that only fetch content on demand lead nowhere else.
    internal static bool IsLink(string folder)
    {
        try
        {
            var info = new DirectoryInfo(folder);
            return info.Attributes != (FileAttributes)(-1) && (info.Attributes & FileAttributes.ReparsePoint) != 0 && info.LinkTarget is not null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
