using Savepoint.Core.Errors;

namespace Savepoint.Core.Git;

// Turns what git said into the typed errors the rest of the app understands. git's messages are English: GitBinary pins LC_ALL=C.
internal static class GitFailure
{
    // Another git process (or a virus scanner) holds a lock file for a moment; trying again shortly usually works.
    public static bool IsLockContention(string standardError) =>
        standardError.Contains(".lock': File exists", StringComparison.Ordinal)
        || standardError.Contains("Another git process seems to be running", StringComparison.Ordinal);

    public static bool IsDiskFull(string standardError) =>
        standardError.Contains("No space left on device", StringComparison.Ordinal)
        || standardError.Contains("There is not enough space on the disk", StringComparison.Ordinal)
        || standardError.Contains("Disk quota exceeded", StringComparison.Ordinal);

    public static bool IsAccessDenied(string standardError) =>
        standardError.Contains("Permission denied", StringComparison.Ordinal)
        || standardError.Contains("Access is denied", StringComparison.Ordinal);

    // git's index file (the list of what the last snapshot held) is empty, short or unreadable, which is what a power loss right after a
    // write leaves behind. The phrases are the whole sentence git prints for the index itself; a pack index says "is too small".
    public static bool IsIndexDamage(string standardError) =>
        standardError.Contains("index file smaller than expected", StringComparison.Ordinal)
        || standardError.Contains("index file corrupt", StringComparison.Ordinal)
        || standardError.Contains("bad index file sha1 signature", StringComparison.Ordinal)
        || standardError.Contains("unknown index entry format", StringComparison.Ordinal);

    // The files git lists when it refuses a checkout because they would be overwritten: indented lines under the heading.
    public static IReadOnlyList<string> OverwrittenPaths(string standardError)
    {
        var paths = new List<string>();
        var listing = false;
        foreach (var line in standardError.Split('\n'))
        {
            if (IsRefusalHeading(line))
            {
                listing = true;
            }
            else if (listing && line.StartsWith('\t'))
            {
                paths.Add(line.Trim());
            }
            else
            {
                listing = false;
            }
        }

        return paths;
    }

    public static bool RefusedToOverwrite(string standardError) =>
        standardError.Split('\n').Any(IsRefusalHeading);

    // git could not remove or replace a file (another program has it open); the rest of the command was carried out.
    public static bool FileCouldNotBeReplaced(string standardError) =>
        standardError.Contains("unable to unlink old", StringComparison.Ordinal);

    // The lines under which git lists what is in the way: local changes or untracked files that would be overwritten, removed, or lost with a folder.
    private static bool IsRefusalHeading(string line) =>
        line.Contains("would be overwritten by checkout", StringComparison.Ordinal)
        || line.Contains("would be removed by checkout", StringComparison.Ordinal)
        || line.Contains("would lose untracked files in them", StringComparison.Ordinal);

    // The files git says it could not add, in the order it says so: "error: unable to index file '<path>'", the path relative to the work tree.
    public static IReadOnlyList<string> UnindexedPaths(string text)
    {
        const string marker = "unable to index file '";
        var paths = new List<string>();
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            var start = trimmed.IndexOf(marker, StringComparison.Ordinal);
            if (start >= 0 && trimmed.EndsWith('\'') && trimmed.Length > start + marker.Length)
            {
                paths.Add(trimmed[(start + marker.Length)..^1]);
            }
        }

        return paths;
    }

    // The lock file git names when it cannot take a lock, if it names one.
    public static string? LockFilePath(string standardError)
    {
        const string marker = "Unable to create '";
        var start = standardError.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += marker.Length;
        var end = standardError.IndexOf("':", start, StringComparison.Ordinal);
        return end > start ? standardError[start..end] : null;
    }

    // A failure nothing more specific explains; the developer message carries git's own words.
    public static GitOperationException ToException(GitCommandException e) => new(e.Message) { StandardError = e.StandardError };
}
