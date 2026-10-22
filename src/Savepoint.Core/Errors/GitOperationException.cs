namespace Savepoint.Core.Errors;

/// <summary>
/// A git operation could not finish. Two ids tell the user apart what went wrong: a file that stayed locked by a running program
/// (<c>error.gitOperation.checkoutFailed</c>, args <c>path</c>), and any other failure of the saved history, such as a missing
/// repository or a point that is not there (<c>error.gitOperation.failed</c>, no args).
/// </summary>
public sealed class GitOperationException : SavepointException
{
    /// <summary>Creates the error for a file that stayed locked.</summary>
    /// <param name="path">The file that stayed locked, or null when git did not name one.</param>
    /// <param name="devMessage">English text for the log: what git reported.</param>
    public GitOperationException(string? path, string devMessage)
        : base(
            "error.gitOperation.checkoutFailed",
            devMessage,
            new Dictionary<string, object?> { ["path"] = path })
    {
    }

    /// <summary>Creates the error for a failure that is not a locked file.</summary>
    /// <param name="devMessage">English text for the log: what went wrong.</param>
    public GitOperationException(string devMessage)
        : base("error.gitOperation.failed", devMessage)
    {
    }
}
