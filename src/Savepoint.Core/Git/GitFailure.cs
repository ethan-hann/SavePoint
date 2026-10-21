using Savepoint.Core.Errors;

namespace Savepoint.Core.Git;

// Turns what git said into the typed errors the rest of the app understands.
internal static class GitFailure
{
    // Another git process (or a virus scanner) holds a lock file for a moment; trying again shortly usually works.
    public static bool IsLockContention(string standardError) =>
        standardError.Contains(".lock': File exists", StringComparison.Ordinal)
        || standardError.Contains("Another git process seems to be running", StringComparison.Ordinal);

    // A failure nothing more specific explains; the developer message carries git's own words.
    public static GitOperationException ToException(GitCommandException e) => new(null, e.Message);
}
