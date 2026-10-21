namespace Savepoint.Core.Errors;

public sealed class GitOperationException : SavepointException
{
    public GitOperationException(string? path, string devMessage)
        : base(
            "error.gitOperation.checkoutFailed",
            devMessage,
            new Dictionary<string, object?> { ["path"] = path })
    {
    }
}
