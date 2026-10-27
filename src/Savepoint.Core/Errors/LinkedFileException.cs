namespace Savepoint.Core.Errors;

/// <summary>
/// A file inside the game is a symbolic link to a file somewhere else (a mod manager's link deployment makes these). Git for
/// Windows records such a link as the text of its target and writes that text back as an ordinary file, which would replace
/// the link and break the deployment, so nothing was staged or written. Raised on Windows only; on Linux and macOS a link is
/// stored and restored as a link.
/// </summary>
public sealed class LinkedFileException : SavepointException
{
    /// <summary>Builds the error for the first linked file found.</summary>
    /// <param name="path">The linked file, absolute.</param>
    public LinkedFileException(string path)
        : base(
            "error.capture.linkedFile",
            $"'{path}' is a link to another file, which a capture or a switch cannot store or restore as a link.",
            new Dictionary<string, object?> { ["path"] = path, ["name"] = ErrorPathName.LastSegment(path) })
    {
    }
}
