namespace Savepoint.Core.Errors;

public sealed class PathMissingException : SavepointException
{
    public PathMissingException(string path, string role)
        : base(
            "error.path.missing",
            $"The {role} path '{path}' does not exist.",
            new Dictionary<string, object?> { ["path"] = path, ["role"] = role })
    {
    }
}
