namespace Savepoint.Core.Errors;

/// <summary>
/// "Start with an empty folder" was asked for, but the platform's default storage folder already holds saved history, so it is not
/// empty (<c>error.storage.defaultFolderHasHistory</c>). The way on is "Find the folder", pointed at that folder. Nothing changed. Args: <c>path</c>.
/// </summary>
public sealed class StorageDefaultFolderHasHistoryException : SavepointException
{
    /// <summary>Creates the error.</summary>
    /// <param name="path">The default storage folder that holds history.</param>
    public StorageDefaultFolderHasHistoryException(string path)
        : base(
            "error.storage.defaultFolderHasHistory",
            $"The default storage folder '{path}' already holds saved history, so it cannot be used as an empty start.",
            new Dictionary<string, object?> { ["path"] = path })
    {
    }
}
