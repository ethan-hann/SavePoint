using Savepoint.Core.Errors;

namespace Savepoint.Core.Services.Storage;

/// <summary>One thing inside SavePoint's storage: a folder (empty ones too), a file, or a link.</summary>
/// <param name="RelativePath">The path below the storage root, with the OS separator.</param>
/// <param name="IsFolder">True for a folder, which is made before anything in it.</param>
/// <param name="Length">The size of a file; zero for a folder or a link.</param>
/// <param name="LinkTarget">Where a link points, as written; null for anything that is not a link.</param>
internal sealed record ContentEntry(string RelativePath, bool IsFolder, long Length, string? LinkTarget);

/// <summary>Everything a storage move or a clear-all has to deal with, listed once.</summary>
/// <param name="Folders">The SavePoint folders directly under the root.</param>
/// <param name="Entries">Every folder, file and link inside them, each folder before its contents.</param>
internal sealed record ContentListing(IReadOnlyList<string> Folders, IReadOnlyList<ContentEntry> Entries)
{
    public int FileCount => Entries.Count(entry => entry.LinkTarget is not null || !entry.IsFolder);

    public long Bytes => Entries.Sum(entry => entry.Length);
}

/// <summary>Finds what SavePoint itself keeps in a storage root, and nothing else a user may have put there.</summary>
internal static class StorageContent
{
    // Hidden and system entries are real storage; the default options would skip them. A strict walk does not skip what it cannot open.
    private static EnumerationOptions Everything(bool strict) => new()
    {
        AttributesToSkip = 0,
        IgnoreInaccessible = !strict,
        RecurseSubdirectories = false,
    };

    /// <summary>The folders directly under the root that hold a game's git directories, which is how SavePoint's own folders are told from anything else.</summary>
    /// <param name="root">The storage root.</param>
    /// <param name="strict">True to fail on a folder that cannot be opened (a move must not leave one behind) instead of passing over it.</param>
    /// <exception cref="StorageRelocationFailedException">Strict, and a folder under the root cannot be opened.</exception>
    public static IReadOnlyList<string> Folders(string root, bool strict = false)
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        return
        [
            .. new DirectoryInfo(root).EnumerateDirectories("*", Everything(strict))
                .Where(folder => !folder.Attributes.HasFlag(FileAttributes.ReparsePoint))
                .Where(folder => strict ? HoldsGitDirectories(folder) : Directory.Exists(Path.Combine(folder.FullName, "install.git")) || Directory.Exists(Path.Combine(folder.FullName, "external.git")))
                .Select(folder => folder.FullName)
                .Order(StringComparer.Ordinal),
        ];
    }

    // Looks inside, so a folder that cannot be opened is an error here and not a folder that "holds nothing".
    private static bool HoldsGitDirectories(DirectoryInfo folder)
    {
        try
        {
            return folder.EnumerateDirectories("*", Everything(strict: true)).Any(child => child.Name is "install.git" or "external.git");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw Unreadable(folder.FullName, e);
        }
    }

    /// <summary>Lists SavePoint's folders and everything in them. Links are listed as links and never entered.</summary>
    /// <param name="root">The storage root.</param>
    /// <param name="cancellationToken">Stops the walk between entries.</param>
    /// <param name="strict">True to fail on any entry that cannot be read, naming it, instead of leaving it out of the listing.</param>
    /// <exception cref="StorageRelocationFailedException">Strict, and an entry cannot be read.</exception>
    public static ContentListing Scan(string root, CancellationToken cancellationToken, bool strict = false)
    {
        var folders = Folders(root, strict);
        var entries = new List<ContentEntry>();
        foreach (var folder in folders)
        {
            Walk(root, new DirectoryInfo(folder), entries, strict, cancellationToken);
        }

        return new ContentListing(folders, entries);
    }

    private static void Walk(string root, DirectoryInfo folder, List<ContentEntry> entries, bool strict, CancellationToken cancellationToken)
    {
        entries.Add(new ContentEntry(Path.GetRelativePath(root, folder.FullName), true, 0, null));
        try
        {
            foreach (var entry in folder.EnumerateFileSystemInfos("*", Everything(strict)).OrderBy(entry => entry.Name, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(root, entry.FullName);
                if (entry.LinkTarget is { } target)
                {
                    entries.Add(new ContentEntry(relative, entry is DirectoryInfo, 0, target));
                }
                else if (entry is DirectoryInfo directory)
                {
                    Walk(root, directory, entries, strict, cancellationToken);
                }
                else if (entry is FileInfo file)
                {
                    entries.Add(new ContentEntry(relative, false, file.Length, null));
                }
            }
        }
        catch (Exception e) when (strict && e is IOException or UnauthorizedAccessException)
        {
            throw Unreadable(folder.FullName, e);
        }
    }

    private static StorageRelocationFailedException Unreadable(string path, Exception inner) =>
        new(path, $"'{path}' in the storage could not be read, so it would not be copied: {inner.Message}", inner);
}
