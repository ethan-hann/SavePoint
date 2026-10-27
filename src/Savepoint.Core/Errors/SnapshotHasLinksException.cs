namespace Savepoint.Core.Errors;

/// <summary>
/// A snapshot holds a file as a symbolic link (git mode 120000), which happens when it was taken while a program linked its files
/// into the game folder, or on Linux or macOS. Git for Windows writes such an entry back as a text file holding the link's path, which
/// would break the file, so a switch to the snapshot, or a discard that would restore the entry, is refused before anything is written.
/// Raised on Windows only; on Linux and macOS the link is restored as a link. The player's other profiles stay usable.
/// </summary>
public sealed class SnapshotHasLinksException : SavepointException
{
    /// <summary>Builds the error for the first link the snapshot holds among the files the operation would write.</summary>
    /// <param name="path">The first such file, absolute, as it would be written in the game folder.</param>
    public SnapshotHasLinksException(string path)
        : base(
            "error.switch.snapshotHasLinks",
            $"The snapshot holds '{path}' as a link to another file, which cannot be restored as a link on Windows.",
            new Dictionary<string, object?> { ["path"] = path, ["name"] = ErrorPathName.LastSegment(path) })
    {
    }
}
