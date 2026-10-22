namespace Savepoint.Core.Errors;

/// <summary>
/// The folder chosen as the new storage location lies inside the folder SavePoint is installed in, which an update or an
/// uninstall deletes with everything in it (<c>error.storageRelocation.insideInstallFolder</c>). Args: <c>path</c>.
/// </summary>
public sealed class StorageInsideInstallFolderException : SavepointException
{
    /// <summary>Creates the error.</summary>
    /// <param name="path">The destination folder that was refused.</param>
    /// <param name="installFolder">The install folder it lies in.</param>
    public StorageInsideInstallFolderException(string path, string installFolder)
        : base(
            "error.storageRelocation.insideInstallFolder",
            $"The storage destination '{path}' is inside the install folder '{installFolder}', which an update or an uninstall deletes.",
            new Dictionary<string, object?> { ["path"] = path })
    {
    }
}
