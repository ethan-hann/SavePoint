using Savepoint.Core.Catalog;

namespace Savepoint.Core.Errors;

/// <summary>
/// A folder has changes that are not saved, and switching would overwrite them (<c>error.launch.dirtyWorkingTree</c>). The launch
/// flow resolves unsaved changes first (D13.8); the engine refuses on its own too (D29). Args: <c>role</c>.
/// </summary>
public sealed class DirtyWorkingTreeException : SavepointException
{
    /// <summary>Creates the error.</summary>
    /// <param name="role">Which of the game's folders has the unsaved changes.</param>
    public DirtyWorkingTreeException(RepoRole role)
        : base(
            "error.launch.dirtyWorkingTree",
            $"The {ErrorArgs.NameOf(role)} folder has changes that are not captured.",
            new Dictionary<string, object?> { ["role"] = role })
    {
    }
}
