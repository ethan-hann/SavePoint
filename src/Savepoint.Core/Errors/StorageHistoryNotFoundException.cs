namespace Savepoint.Core.Errors;

/// <summary>
/// The folder chosen as the place where SavePoint's saved history now is does not hold the history of the games in the catalog
/// (<c>error.storage.noHistoryHere</c>). Nothing changed. Args: <c>path</c>.
/// </summary>
public sealed class StorageHistoryNotFoundException : SavepointException
{
    /// <summary>Creates the error.</summary>
    /// <param name="path">The folder that was chosen.</param>
    public StorageHistoryNotFoundException(string path)
        : base(
            "error.storage.noHistoryHere",
            $"The folder '{path}' holds no saved history for the games in the catalog.",
            new Dictionary<string, object?> { ["path"] = path })
    {
    }
}
