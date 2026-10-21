using Savepoint.Core.Catalog;

namespace Savepoint.Core.Errors;

public sealed class PathMissingException : SavepointException
{
    public PathMissingException(string path, RepoRole role)
        : base(
            "error.path.missing",
            $"The {ErrorArgs.NameOf(role)} path '{path}' does not exist.",
            new Dictionary<string, object?> { ["path"] = path, ["role"] = role })
    {
    }
}
