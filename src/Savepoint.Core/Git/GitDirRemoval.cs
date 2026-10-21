using System.Text.RegularExpressions;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Git;

// Deletes a git directory, and refuses anything that does not look like one: the path comes from a caller, and a recursive delete of the wrong folder cannot be undone.
internal static partial class GitDirRemoval
{
    // What a git directory (even a half-deleted one) is made of; one of these must be left for the folder to count as ours.
    private static readonly HashSet<string> _knownEntries = new(StringComparer.OrdinalIgnoreCase)
    {
        "HEAD", "config", "description", "hooks", "info", "objects", "refs", "logs", "index", "packed-refs",
        "COMMIT_EDITMSG", "ORIG_HEAD", "FETCH_HEAD", "gc.log", "shallow", "savepoint-switch.json",
    };

    public static async Task RemoveAsync(string gitDir, IReadOnlyList<TimeSpan> retryDelays, CancellationToken ct)
    {
        if (!Directory.Exists(gitDir))
        {
            return;
        }

        if (!LooksLikeGitDir(gitDir))
        {
            throw new GitOperationException(gitDir, $"'{gitDir}' is not a git directory, so it was not deleted.");
        }

        for (var attempt = 0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                ClearReadOnly(gitDir);
                Directory.Delete(gitDir, recursive: true);
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (attempt >= retryDelays.Count)
                {
                    throw new GitOperationException(LockedPath(e) ?? gitDir, $"The git directory '{gitDir}' could not be deleted: {e.Message}");
                }

                // A process that was just stopped can hold a file for a moment longer.
                await Task.Delay(retryDelays[attempt], ct).ConfigureAwait(false);
            }
        }
    }

    internal static bool LooksLikeGitDir(string path)
    {
        var any = false;
        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            any = true;
            var name = Path.GetFileName(entry);
            if (_knownEntries.Contains(name) || name.EndsWith(".lock", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // Nothing left is nothing to protect.
        return !any;
    }

    // Pack files are read-only, and Windows will not delete a read-only file.
    private static void ClearReadOnly(string folder)
    {
        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
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
