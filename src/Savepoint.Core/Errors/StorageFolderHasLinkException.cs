namespace Savepoint.Core.Errors;

/// <summary>
/// The folder chosen as the place where SavePoint's saved history now is has a link in it (a junction or a symbolic link) where SavePoint
/// would read or write: a game's folder, a git directory, or a file or folder inside one. A link leads wherever its maker pointed it, so
/// the whole folder is refused before anything in it is touched (<c>error.storage.folderHasLink</c>). Nothing changed. Args: <c>path</c>,
/// the link, or the path that leads outside the folder.
/// </summary>
public sealed class StorageFolderHasLinkException : SavepointException
{
    /// <summary>Creates the error.</summary>
    /// <param name="path">The link, or the path that does not stay inside the folder that was chosen.</param>
    public StorageFolderHasLinkException(string path)
        : base(
            "error.storage.folderHasLink",
            $"'{path}' is a link to another place, so the storage folder holding it was not used.",
            new Dictionary<string, object?> { ["path"] = path })
    {
    }
}
