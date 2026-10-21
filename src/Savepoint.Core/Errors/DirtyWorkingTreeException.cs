namespace Savepoint.Core.Errors;

public sealed class DirtyWorkingTreeException : SavepointException
{
    public DirtyWorkingTreeException(string role)
        : base(
            "error.launch.dirtyWorkingTree",
            $"The {role} folder has changes that are not captured.",
            new Dictionary<string, object?> { ["role"] = role })
    {
    }
}
