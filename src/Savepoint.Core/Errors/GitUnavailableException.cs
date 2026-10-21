namespace Savepoint.Core.Errors;

public sealed class GitUnavailableException : SavepointException
{
    public GitUnavailableException(string path, string devMessage, Exception? inner)
        : base(
            "error.git.unavailable",
            devMessage,
            new Dictionary<string, object?> { ["path"] = path },
            inner)
    {
    }
}
