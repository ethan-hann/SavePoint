using Savepoint.Core.Errors;

namespace Savepoint.Core.Git;

// Turns what git said into the typed errors the rest of the app understands.
internal static class GitFailure
{
    // A failure nothing more specific explains; the developer message carries git's own words.
    public static GitOperationException ToException(GitCommandException e) => new(null, e.Message);
}
