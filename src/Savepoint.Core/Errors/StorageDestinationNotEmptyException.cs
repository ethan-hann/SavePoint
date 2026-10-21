namespace Savepoint.Core.Errors;

/// <summary>
/// The folder chosen as the new storage location already has content, so a move would mix it with SavePoint's data
/// (<c>error.storageRelocation.destinationNotEmpty</c>). Args: <c>path</c>.
/// </summary>
public sealed class StorageDestinationNotEmptyException : SavepointException
{
    /// <summary>Creates the error.</summary>
    /// <param name="path">The destination folder that is not empty.</param>
    public StorageDestinationNotEmptyException(string path)
        : base(
            "error.storageRelocation.destinationNotEmpty",
            $"The storage destination '{path}' is not empty.",
            new Dictionary<string, object?> { ["path"] = path })
    {
    }
}
