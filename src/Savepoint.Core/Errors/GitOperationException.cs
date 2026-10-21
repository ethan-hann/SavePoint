namespace Savepoint.Core.Errors;

/// <summary>
/// A git command failed, or an engine operation could not finish, for a reason with no error of its own: a file another
/// program holds open, a lock that never cleared, a request that does not fit the repository's state. The developer message
/// carries git's own words. The message id says that a file may be in use, so a failure with another cause should get a typed error instead.
/// </summary>
public sealed class GitOperationException : SavepointException
{
    /// <summary>Builds the error.</summary>
    /// <param name="path">The file that stayed in use or could not be updated, absolute; null when no single file is to blame.</param>
    /// <param name="devMessage">What failed, in English, for logs.</param>
    public GitOperationException(string? path, string devMessage)
        : base(
            "error.gitOperation.checkoutFailed",
            devMessage,
            new Dictionary<string, object?> { ["path"] = path })
    {
    }
}
