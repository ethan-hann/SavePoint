using System.Text.RegularExpressions;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Git;

// Deletes a git directory, and refuses anything that is not one of SavePoint's: the path comes from a caller, and a recursive delete of the wrong folder cannot be undone.
internal static partial class GitDirRemoval
{
    // What a git directory (even a half-deleted one) is made of, and whether each entry is a folder.
    private static readonly Dictionary<string, bool> _knownEntries = new(StringComparer.OrdinalIgnoreCase)
    {
        ["HEAD"] = false,
        ["config"] = false,
        ["description"] = false,
        ["hooks"] = true,
        ["info"] = true,
        ["objects"] = true,
        ["refs"] = true,
        ["logs"] = true,
        ["index"] = false,
        ["packed-refs"] = false,
        ["COMMIT_EDITMSG"] = false,
        ["ORIG_HEAD"] = false,
        ["FETCH_HEAD"] = false,
        ["gc.log"] = false,
        ["shallow"] = false,
        [SwitchJournal.FileName] = false,
    };

    // Removes the folder when it lies inside the storage root and is a git directory SavePoint made (or what an interrupted
    // delete left of one); anything else is refused and nothing is touched.
    public static async Task RemoveAsync(string gitDir, string storageRoot, Func<string, string> realPath, IReadOnlyList<TimeSpan> retryDelays, CancellationToken ct, Action<string>? beforeDelete = null)
    {
        if (!Directory.Exists(gitDir))
        {
            return;
        }

        // Both the path as given and where it really leads must be below the storage root, so a link cannot lead a delete out of it.
        if (!IsBelow(storageRoot, gitDir) || !IsBelow(realPath(storageRoot), realPath(gitDir)))
        {
            throw new GitOperationException($"'{gitDir}' is not inside the storage folder, so it was not deleted.");
        }

        if (!IsOursOrWhatIsLeftOfIt(gitDir))
        {
            throw new GitOperationException($"'{gitDir}' is not a git directory made by SavePoint, so it was not deleted.");
        }

        for (var attempt = 0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                beforeDelete?.Invoke(gitDir);
                ClearReadOnly(gitDir);
                Directory.Delete(gitDir, recursive: true);
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (attempt >= retryDelays.Count)
                {
                    throw DeleteFailure(gitDir, e);
                }

                // A process that was just stopped can hold a file for a moment longer.
                await Task.Delay(retryDelays[attempt], ct).ConfigureAwait(false);
            }
        }
    }

    // Strictly below: the root itself, and a folder that only starts with the root's name, do not count.
    internal static bool IsBelow(string root, string path)
    {
        var relative = Path.GetRelativePath(Path.TrimEndingDirectorySeparator(root), Path.TrimEndingDirectorySeparator(path));
        return relative != "."
            && !Path.IsPathRooted(relative)
            && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }

    // Every entry is one a git directory has, in its usual kind, and a config file that is there carries SavePoint's stamp.
    // A folder missing HEAD, objects or the config is what an interrupted delete leaves, and is finished off.
    internal static bool IsOursOrWhatIsLeftOfIt(string path)
    {
        var entries = Directory.EnumerateFileSystemEntries(path).ToArray();
        foreach (var entry in entries)
        {
            var name = Path.GetFileName(entry);
            if (name.EndsWith(".lock", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            {
                if (Directory.Exists(entry))
                {
                    return false;
                }

                continue;
            }

            if (!_knownEntries.TryGetValue(name, out var isFolder) || isFolder != Directory.Exists(entry))
            {
                return false;
            }
        }

        var config = Path.Combine(path, "config");
        return !File.Exists(config) || (AtomicText.ReadOrNull(config) is { } text && RepoConfigFile.ReadStamp(text) is not null);
    }

    // A file another program holds open is the one failure that closing programs fixes: it is a sharing or lock violation. Access
    // denied, a read-only volume and the like name a path too, but waiting or closing the game does not help.
    internal static SavepointException DeleteFailure(string path, Exception e)
    {
        // The system sometimes names only the file, which says little on a card; then the folder being deleted is the better path.
        var named = LockedPath(e) is { } quoted && Path.IsPathRooted(quoted) ? quoted : path;
        return e is IOException held && FileLockProbe.IsSharingViolation(held)
            ? new GitOperationException(named, $"'{named}' is held by another program and could not be deleted: {e.Message}")
            : new StorageRemoveFailedException(named, $"'{named}' could not be deleted: {e.Message}", e);
    }

    // Pack files are read-only, and Windows will not delete a read-only file. A link in the git directory is deleted as a link; what it
    // leads to is not ours to change.
    private static void ClearReadOnly(string folder)
    {
        foreach (var file in Directory.EnumerateFiles(folder, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false }))
        {
            if ((File.GetAttributes(file) & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
        }
    }

    private static string? LockedPath(Exception e) =>
        QuotedPath().Match(e.Message) is { Success: true } match ? match.Groups["path"].Value : null;

    [GeneratedRegex("'(?<path>[^']+)'")]
    private static partial Regex QuotedPath();
}
