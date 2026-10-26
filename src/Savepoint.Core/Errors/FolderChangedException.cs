using Savepoint.Core.Catalog;

namespace Savepoint.Core.Errors;

/// <summary>
/// A game folder was replaced, moved, or re-linked, or lost its consent marker, between the moment SavePoint approved it and a step
/// that would delete or overwrite files in it (<c>error.path.changed</c>). The step was not run, so nothing outside the folder
/// SavePoint approved was touched; steps that finished before the change are not undone. Args: <c>path</c> and <c>role</c>.
/// </summary>
public sealed class FolderChangedException : SavepointException
{
    /// <summary>Creates the error.</summary>
    /// <param name="path">The folder as the game's settings spell it.</param>
    /// <param name="role">Which of the game's folders changed.</param>
    public FolderChangedException(string path, RepoRole role)
        : base(
            "error.path.changed",
            $"The {ErrorArgs.NameOf(role)} folder '{path}' was replaced or moved while SavePoint was working on it, so SavePoint stopped before changing more files.",
            new Dictionary<string, object?> { ["path"] = path, ["role"] = role })
    {
    }
}
