namespace Savepoint.Core.Errors;

/// <summary>The bundled git cannot be run: it is missing, not executable, or does not answer like git.</summary>
public sealed class GitUnavailableException : SavepointException
{
    /// <summary>Builds the error.</summary>
    /// <param name="path">The git executable that was expected, absolute.</param>
    /// <param name="devMessage">What was wrong, in English, for logs.</param>
    /// <param name="inner">The failure behind it, when there is one.</param>
    public GitUnavailableException(string path, string devMessage, Exception? inner)
        : base(
            "error.git.unavailable",
            devMessage,
            new Dictionary<string, object?> { ["path"] = path },
            inner)
    {
    }
}
