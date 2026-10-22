using Savepoint.Core.Backend;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Services.Storage;

/// <summary>Deletes and measures the folders SavePoint keeps under its storage root.</summary>
internal static class StorageFolders
{
    private static readonly TimeSpan[] _retryDelays =
    [
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromMilliseconds(1000),
    ];

    /// <summary>
    /// Deletes a folder inside the storage root with everything in it, retrying while a process that was just stopped lets go of
    /// a file. A folder outside the root is never touched. Read-only files (git's packs) are made writable first.
    /// </summary>
    /// <param name="storage">The storage the folder must lie in.</param>
    /// <param name="folder">The folder to delete; a missing folder is fine.</param>
    /// <param name="cancellationToken">Stops the retries.</param>
    /// <exception cref="InvalidOperationException">The folder is the storage root itself or lies outside it.</exception>
    /// <exception cref="GitOperationException">A file stayed in use through every attempt.</exception>
    public static async Task DeleteAsync(StorageSnapshot storage, string folder, CancellationToken cancellationToken)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        var relative = Path.GetRelativePath(storage.Root, full);
        if (relative is "." || relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            throw new InvalidOperationException($"'{folder}' is not inside the storage root, so it was not deleted.");
        }

        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                DeleteTree(full);
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (attempt >= _retryDelays.Length)
                {
                    throw new GitOperationException(full, $"'{full}' could not be deleted: {e.Message}");
                }

                await Task.Delay(_retryDelays[attempt], cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Deletes a folder that holds nothing; a folder with anything in it, or one that is gone or cannot be deleted, is left as it is.</summary>
    /// <param name="folder">The folder to delete.</param>
    internal static void RemoveIfEmpty(string folder)
    {
        try
        {
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
            {
                Directory.Delete(folder);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // An empty folder left behind is harmless.
        }
    }

    // Links inside the folder are removed as links, so deleting can never walk out of it.
    internal static void DeleteTree(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos("*", new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = true }))
        {
            if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                entry.Delete();
            }
            else if (entry is DirectoryInfo directory)
            {
                DeleteTree(directory.FullName);
            }
            else
            {
                if ((entry.Attributes & FileAttributes.ReadOnly) != 0)
                {
                    entry.Attributes = FileAttributes.Normal;
                }

                entry.Delete();
            }
        }

        Directory.Delete(folder);
    }
}
