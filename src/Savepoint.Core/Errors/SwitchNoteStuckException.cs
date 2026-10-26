using Savepoint.Core.Catalog;

namespace Savepoint.Core.Errors;

/// <summary>
/// A discard put the folder back, but SavePoint's note that the last switch did not finish could not be removed
/// (<c>error.launch.switchNoteStuck</c>), usually because another program holds it open. While the note stays, the folder still counts as
/// half switched. Discarding again removes it once the other program lets go. Args: <c>role</c> and <c>path</c> (the note).
/// </summary>
public sealed class SwitchNoteStuckException : SavepointException
{
    /// <summary>Creates the error.</summary>
    /// <param name="role">Which of the game's folders the note belongs to.</param>
    /// <param name="path">The note file.</param>
    public SwitchNoteStuckException(RepoRole role, string path)
        : base(
            "error.launch.switchNoteStuck",
            $"The files of the {ErrorArgs.NameOf(role)} folder were put back, but the note at '{path}' that its last switch did not finish could not be removed.",
            new Dictionary<string, object?> { ["role"] = role, ["path"] = path })
    {
    }
}
