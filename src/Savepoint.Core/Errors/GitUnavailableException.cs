namespace Savepoint.Core.Errors;

/// <summary>
/// The bundled git cannot be found or started (<c>error.git.unavailable</c>). The startup check turns it into the error screen that
/// names what to reinstall (D18). Args: <c>path</c>, the git executable that was tried.
/// </summary>
public sealed class GitUnavailableException : SavepointException
{
    /// <summary>Creates the error.</summary>
    /// <param name="path">The git executable that was tried.</param>
    /// <param name="devMessage">English text for the log: what was missing or why the start failed.</param>
    /// <param name="inner">The exception the failed start threw, when there was one.</param>
    public GitUnavailableException(string path, string devMessage, Exception? inner)
        : base(
            "error.git.unavailable",
            devMessage,
            new Dictionary<string, object?> { ["path"] = path },
            inner)
    {
    }
}
