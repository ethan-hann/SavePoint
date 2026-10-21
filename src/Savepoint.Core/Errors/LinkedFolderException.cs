namespace Savepoint.Core.Errors;

/// <summary>
/// A folder inside the game is a link (a junction or a folder symbolic link) to somewhere else. Saving or restoring files
/// through it could read, overwrite or delete files outside the game folder, so nothing was staged or written. Raised on
/// Windows only; on Linux and macOS a link is stored as a link and never followed.
/// </summary>
public sealed class LinkedFolderException : SavepointException
{
    /// <summary>Builds the error for the first linked folder found.</summary>
    /// <param name="path">The linked folder, absolute.</param>
    public LinkedFolderException(string path)
        : base(
            "error.capture.linkedFolder",
            $"'{path}' is a link to another folder, which a capture or a switch cannot go through safely.",
            new Dictionary<string, object?> { ["path"] = path })
    {
    }
}
