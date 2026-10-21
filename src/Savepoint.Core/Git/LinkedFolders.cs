using Savepoint.Core.Errors;

namespace Savepoint.Core.Git;

// Git for Windows follows junctions and folder symbolic links as if they were ordinary folders, so a switch or a discard
// could overwrite or delete files outside the game folder through one. On Windows every operation looks at the folders
// that hold the files it is about to read, write or remove, and refuses before touching anything. Elsewhere git stores a
// link as a link and never goes through it.
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

    // Throws for the first linked folder (in path order) that holds one of the files; nothing is read or written.
    public static void RefuseAny(string workTree, IEnumerable<string> files, CancellationToken ct)
    {
        if (!Enabled)
        {
            return;
        }

        var folders = FoldersOf(files);
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
