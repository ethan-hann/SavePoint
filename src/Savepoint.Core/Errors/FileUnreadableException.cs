namespace Savepoint.Core.Errors;

/// <summary>
/// A file in a game folder cannot be read (access is denied), so SavePoint cannot tell whether it changed and the folder cannot be
/// switched or saved until it can (<c>error.gitOperation.unreadableFile</c>). Closing programs does not help, unlike a file that is
/// in use. Args: <c>path</c>.
/// </summary>
public sealed class FileUnreadableException : SavepointException
{
    /// <summary>Creates the error.</summary>
    /// <param name="path">The file that cannot be read.</param>
    public FileUnreadableException(string path)
        : base(
            "error.gitOperation.unreadableFile",
            $"'{path}' cannot be read, so it could not be checked for changes.",
            new Dictionary<string, object?> { ["path"] = path })
    {
    }
}
