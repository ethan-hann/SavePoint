using Savepoint.Core.Catalog;

namespace Savepoint.Core.Errors;

/// <summary>
/// A folder or file SavePoint needs is not there: a game folder that was moved or deleted, or the program a launch should start
/// (reported with <see cref="RepoRole.Install"/>). Id: <c>error.path.missing</c>. Args: <c>path</c>, <c>role</c>.
/// </summary>
public sealed class PathMissingException : SavepointException
{
    /// <summary>Creates the error.</summary>
    /// <param name="path">The resolved path that does not exist.</param>
    /// <param name="role">Which of the game's folders the path belongs to.</param>
    public PathMissingException(string path, RepoRole role)
        : base(
            "error.path.missing",
            $"The {ErrorArgs.NameOf(role)} path '{path}' does not exist.",
            new Dictionary<string, object?> { ["path"] = path, ["role"] = role })
    {
    }
}
