using System.IO.Enumeration;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Git;

// A file and a folder cannot share a path, and git makes room for one by deleting the other with everything in it. For a folder it counts
// only the files it does not treat as ignored, and its idea of ignored is broader than SavePoint's (a file the player's ignore list names
// is expendable to it, so is one a mod's own ignore file names), so it is never asked. Before anything is written, the file system says
// whether the operation would remove a file of the player's that it does not itself remove.
public sealed partial class GitEngine
{
    private enum DiskEntry
    {
        Missing,
        File,
        Folder,
        FolderLink,
    }

    // A switch writes the files in plan.Written and removes the tracked files in plan.Removed. With nothing unsaved in the way (the dirty
    // check came first) the only other files it can find in a folder are ones the ignore list names, which it leaves alone. A forced switch
    // exists to overwrite new files, so those do not stop it.
    private Task RefuseFileFolderClashAsync(RepoRef repo, SwitchPlan plan, bool force, CancellationToken ct) =>
        RefuseFileFolderClashAsync(repo, plan.Written, plan.Removed, force, ct);

    // A discard writes the files of the point that differ from the disk and removes the new ones that the ignore list does not name.
    private async Task RefuseDiscardClashAsync(RepoRef repo, CancellationToken ct)
    {
        // A path the point lacks is not written by a discard, so only the paths it holds are asked about.
        var result = await RunAsync(Invocation(repo, readOnly: true, "diff", "--name-only", "-z", "--no-renames", "--diff-filter=a", "HEAD", "--"), ct).ConfigureAwait(false);
        var differing = result.SplitStandardOutputOnNul();
        if (differing.Count == 0)
        {
            return;
        }

        var named = await NamedAmongAsync(repo, differing, ct).ConfigureAwait(false);
        await RefuseFileFolderClashAsync(repo, differing.Where(path => !named.Contains(path)).ToHashSet(StringComparer.Ordinal), new HashSet<string>(PathComparer), unsavedFilesGo: true, ct).ConfigureAwait(false);
    }

    // written: the paths the operation will write as files. removed: the paths of tracked files it removes itself.
    // unsavedFilesGo: whether it also removes new files the ignore list does not name (a discard, a forced switch).
    private async Task RefuseFileFolderClashAsync(RepoRef repo, IReadOnlyCollection<string> written, IReadOnlySet<string> removed, bool unsavedFilesGo, CancellationToken ct)
    {
        if (written.Count == 0)
        {
            return;
        }

        // Where a file is to be written and the disk holds a folder: every file in it that is not removed stands in the way.
        var paths = written.Order(StringComparer.Ordinal).ToArray();
        var kinds = ProbeAll(repo, paths, ct);
        var inFolders = new List<(string Folder, List<string> Files)>();
        for (var i = 0; i < paths.Length; i++)
        {
            if (kinds[i] == DiskEntry.Folder)
            {
                inFolders.Add((paths[i], FilesBelow(repo, paths[i], removed)));
            }
        }

        // Where a folder is needed and the disk holds a file.
        var folders = LinkedFolders.FoldersOf(written);
        var folderKinds = ProbeAll(repo, folders, ct);
        var files = folders.Where((_, i) => folderKinds[i] is DiskEntry.File or DiskEntry.FolderLink && !removed.Contains(folders[i])).ToList();

        var candidates = inFolders.SelectMany(entry => entry.Files).Concat(files).ToList();
        if (candidates.Count == 0)
        {
            return;
        }

        // Where new files go (a discard, a forced switch) only a file the ignore list names, or part of a repository of its own, is protected.
        HashSet<string> standInTheWay = [.. candidates];
        if (unsavedFilesGo)
        {
            var named = await NamedAmongAsync(repo, candidates, ct).ConfigureAwait(false);
            standInTheWay = [.. candidates.Where(path => named.Contains(path) || InsideRepository(path))];
        }

        foreach (var (folder, inside) in inFolders)
        {
            if (inside.Where(standInTheWay.Contains).Order(StringComparer.Ordinal).FirstOrDefault() is { } file)
            {
                throw new FileFolderClashException(repo.Role, FileFolderClashKind.FolderHoldsFiles, FullPath(repo, folder), OnDiskPath(repo, file));
            }
        }

        if (files.FirstOrDefault(standInTheWay.Contains) is { } blocking)
        {
            throw new FileFolderClashException(repo.Role, FileFolderClashKind.FileInTheWay, FullPath(repo, blocking), OnDiskPath(repo, blocking));
        }
    }

    private DiskEntry[] ProbeAll(RepoRef repo, IReadOnlyList<string> paths, CancellationToken ct)
    {
        var kinds = new DiskEntry[paths.Count];
        Parallel.For(0, paths.Count, new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Environment.ProcessorCount }, i => kinds[i] = Probe(FullPath(repo, paths[i])));
        for (var i = 0; i < kinds.Length; i++)
        {
            // On Windows git goes through a link, so a switch or a discard never does.
            if (kinds[i] == DiskEntry.FolderLink && LinkedFolders.Enabled)
            {
                throw new LinkedFolderException(FullPath(repo, paths[i]));
            }
        }

        return kinds;
    }

    // A link to a folder is one entry to git where it does not follow links, so it counts as a file there.
    private static DiskEntry Probe(string full)
    {
        try
        {
            var info = new FileInfo(full);
            var attributes = info.Attributes;
            if (attributes == (FileAttributes)(-1))
            {
                return DiskEntry.Missing;
            }

            if ((attributes & FileAttributes.Directory) == 0)
            {
                return DiskEntry.File;
            }

            return (attributes & FileAttributes.ReparsePoint) != 0 && info.LinkTarget is not null ? DiskEntry.FolderLink : DiskEntry.Folder;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // What this cannot read, git cannot write over either.
            return DiskEntry.Missing;
        }
    }

    // The paths (relative to the work tree) of every file under a folder that the operation does not remove, without going through a link.
    // An empty folder holds no file, and a folder that cannot be read is taken to hold one.
    private List<string> FilesBelow(RepoRef repo, string folder, IReadOnlySet<string> removed)
    {
        var root = FullPath(repo, folder);
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = false };
        var below = new FileSystemEnumerable<(string Full, bool Link)>(root, (ref FileSystemEntry entry) => (entry.ToFullPath(), IsFolderLink(ref entry)), options)
        {
            ShouldIncludePredicate = (ref FileSystemEntry entry) => !entry.IsDirectory || IsFolderLink(ref entry),
            ShouldRecursePredicate = (ref FileSystemEntry entry) => !IsFolderLink(ref entry),
        };

        var found = new List<string>();
        try
        {
            foreach (var (full, link) in below)
            {
                if (link && LinkedFolders.Enabled)
                {
                    throw new LinkedFolderException(full);
                }

                var relative = folder + "/" + Path.GetRelativePath(root, full).Replace(Path.DirectorySeparatorChar, '/');
                if (!removed.Contains(relative))
                {
                    found.Add(relative);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new FileFolderClashException(repo.Role, FileFolderClashKind.FolderHoldsFiles, root, root);
        }

        return found;
    }

    // The full path of a work tree path with each part spelled as its folder lists it. A drive that ignores case (NTFS, exFAT, APFS) opens
    // Mods/Foo for a folder on disk named Mods/foo, and the player looks for the file under the name they see, not the point's.
    private static string OnDiskPath(RepoRef repo, string relative)
    {
        var parts = relative.Split('/');
        var folder = repo.WorkTree;
        for (var i = 0; i < parts.Length; i++)
        {
            parts[i] = ListedName(folder, parts[i]);
            folder = Path.Combine(folder, parts[i]);
        }

        return FullPath(repo, string.Join('/', parts));
    }

    // The entry of the folder named like name: the exact spelling when the folder has it (a case-sensitive drive can hold both), else the one
    // that differs only in case. A folder that cannot be listed, or holds no such entry, keeps the spelling it was asked about.
    private static string ListedName(string folder, string name)
    {
        string? differsInCase = null;
        try
        {
            var entries = new FileSystemEnumerable<string>(folder, (ref FileSystemEntry entry) => entry.FileName.ToString(), new EnumerationOptions { AttributesToSkip = 0 });
            foreach (var entry in entries)
            {
                if (string.Equals(entry, name, StringComparison.Ordinal))
                {
                    return entry;
                }

                if (differsInCase is null && string.Equals(entry, name, StringComparison.OrdinalIgnoreCase))
                {
                    differsInCase = entry;
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Left as asked about.
        }

        return differsInCase ?? name;
    }

    private static bool IsFolderLink(ref FileSystemEntry entry) =>
        entry.IsDirectory && (entry.Attributes & FileAttributes.ReparsePoint) != 0 && new DirectoryInfo(entry.ToFullPath()).LinkTarget is not null;
    // A folder with its own history is never part of what a switch writes or removes.
    private static bool InsideRepository(string path) =>
        path.Split('/').Any(part => string.Equals(part, ".git", StringComparison.OrdinalIgnoreCase));
}
