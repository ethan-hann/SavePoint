namespace Savepoint.Core.Errors;

/// <summary>
/// A checkout could not finish, usually because a file stayed locked by a running program (<c>error.gitOperation.checkoutFailed</c>).
/// Args: <c>path</c>, the file that stayed locked, or null when it is not known.
/// </summary>
public sealed class GitOperationException : SavepointException
{
    /// <summary>Creates the error.</summary>
    /// <param name="path">The file that stayed locked, or null when git did not name one.</param>
    /// <param name="devMessage">English text for the log: what git reported.</param>
    public GitOperationException(string? path, string devMessage)
        : base(
            "error.gitOperation.checkoutFailed",
            devMessage,
            new Dictionary<string, object?> { ["path"] = path })
    {
    }
}
