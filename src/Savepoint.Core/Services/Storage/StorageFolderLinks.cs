using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Services.Storage;

/// <summary>
/// Finds the links in a folder that someone else prepared as SavePoint's storage. "Find the folder" rewrites configs and empties hooks
/// folders in what it adopts, and SavePoint writes into the game folders and git directories after that; through a link every one of
/// those is done to files that were never SavePoint's. So a link where SavePoint reads or writes refuses the whole folder.
/// </summary>
internal static class StorageFolderLinks
{
    private static readonly string[] _gitDirectoryNames = ["install.git", "external.git"];

    private static EnumerationOptions Everything => new() { AttributesToSkip = 0, IgnoreInaccessible = true, RecurseSubdirectories = false };

    /// <summary>The first link, or path that leads out of the folder, that SavePoint would read or write; null when there is none.</summary>
    /// <param name="root">The folder, absolute.</param>
    /// <param name="games">The catalog's games: a link at a listed game's folder is refused whatever it leads to, because that folder gets a repository.</param>
    /// <param name="realPath">Follows every link in a path; the folder's own path is judged by what it resolves to.</param>
    public static SavepointException? Refusal(string root, IReadOnlyList<GameDefinition> games, Func<string, string> realPath)
    {
        if (!Directory.Exists(root))
        {
            return null;
        }

        string realRoot;
        try
        {
            realRoot = realPath(root);
        }
        catch (Exception e) when (e is IOException or ArgumentException)
        {
            return new StorageFolderHasLinkException(root);
        }

        foreach (var game in games)
        {
            var folder = Path.Combine(root, game.Id);
            if (IsLink(folder))
            {
                return new StorageFolderHasLinkException(folder);
            }
        }

        foreach (var folder in new DirectoryInfo(root).EnumerateDirectories("*", Everything).OrderBy(folder => folder.Name, StringComparer.Ordinal))
        {
            if (FirstProblemIn(folder, realRoot, realPath) is { } problem)
            {
                return new StorageFolderHasLinkException(problem);
            }
        }

        return null;
    }

    private static string? FirstProblemIn(DirectoryInfo folder, string realRoot, Func<string, string> realPath)
    {
        // A link to a folder of git directories is a game folder, which nothing may reach through; any other link is not SavePoint's.
        if (IsLink(folder))
        {
            return _gitDirectoryNames.Any(name => Directory.Exists(Path.Combine(folder.FullName, name))) ? folder.FullName : null;
        }

        foreach (var name in _gitDirectoryNames)
        {
            var gitDir = Path.Combine(folder.FullName, name);
            if (IsLink(gitDir))
            {
                return gitDir;
            }

            if (!Directory.Exists(gitDir))
            {
                continue;
            }

            if (FirstLinkInside(gitDir) is { } link)
            {
                return link;
            }

            foreach (var path in new[] { folder.FullName, gitDir, Path.Combine(gitDir, "hooks"), Path.Combine(gitDir, "config") })
            {
                if (!StaysInside(realRoot, path, realPath))
                {
                    return path;
                }
            }
        }

        return null;
    }

    // What SavePoint reads, rewrites or empties in a git directory is its direct entries (the config, the hooks folder) and what its
    // info folder holds; any link among them could lead a write or a delete out of the folder. It also writes refs and objects at every
    // save and sweeps lock and temporary files at every start, so a link in any folder below could lead those out of it too.
    private static string? FirstLinkInside(string gitDir)
    {
        var directory = new DirectoryInfo(gitDir);
        foreach (var entry in directory.EnumerateFileSystemInfos("*", Everything).OrderBy(entry => entry.Name, StringComparer.Ordinal))
        {
            if (IsLink(entry))
            {
                return entry.FullName;
            }

            if (entry is DirectoryInfo { Name: "info" } info)
            {
                foreach (var inner in info.EnumerateFileSystemInfos("*", Everything).OrderBy(inner => inner.Name, StringComparer.Ordinal))
                {
                    if (IsLink(inner))
                    {
                        return inner.FullName;
                    }
                }
            }
        }

        return FirstLinkedFolderBelow(directory);
    }

    // The first folder, in name order, that is a link anywhere below; folders only, so a repository of any size is cheap to look through.
    private static string? FirstLinkedFolderBelow(DirectoryInfo folder)
    {
        foreach (var directory in folder.EnumerateDirectories("*", Everything).OrderBy(directory => directory.Name, StringComparer.Ordinal))
        {
            if (IsLink(directory))
            {
                return directory.FullName;
            }

            if (FirstLinkedFolderBelow(directory) is { } deeper)
            {
                return deeper;
            }
        }

        return null;
    }

    // A path that cannot be traced (a link loop) counts as leaving the folder.
    private static bool StaysInside(string realRoot, string path, Func<string, string> realPath)
    {
        try
        {
            return GitDirRemoval.IsBelow(realRoot, realPath(path));
        }
        catch (Exception e) when (e is IOException or ArgumentException)
        {
            return false;
        }
    }

    private static bool IsLink(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            return (attributes & FileAttributes.ReparsePoint) != 0
                && (((attributes & FileAttributes.Directory) != 0 ? new DirectoryInfo(path) : (FileSystemInfo)new FileInfo(path)).LinkTarget is not null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // A reparse point that leads somewhere is a junction or a link; a placeholder that only fetches content on demand leads nowhere else.
    private static bool IsLink(FileSystemInfo info)
    {
        try
        {
            return (info.Attributes & FileAttributes.ReparsePoint) != 0 && info.LinkTarget is not null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
