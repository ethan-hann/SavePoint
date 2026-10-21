using Savepoint.Core.Catalog;

namespace Savepoint.Core.Errors;

public sealed class DirtyWorkingTreeException : SavepointException
{
    public DirtyWorkingTreeException(RepoRole role)
        : base(
            "error.launch.dirtyWorkingTree",
            $"The {ErrorArgs.NameOf(role)} folder has changes that are not captured.",
            new Dictionary<string, object?> { ["role"] = role })
    {
    }
}
