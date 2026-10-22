namespace Savepoint.Core.Errors;

/// <summary>
/// The startup choices for a storage folder that is gone ("Find the folder", "Start with an empty folder") were asked for while the
/// folder SavePoint is set to use can be reached, so there is nothing to find or start over from (<c>error.storage.stillReachable</c>).
/// Nothing changed. Args: <c>path</c>.
/// </summary>
public sealed class StorageStillReachableException : SavepointException
{
    /// <summary>Creates the error.</summary>
    /// <param name="path">The storage folder SavePoint is using, which can be reached.</param>
    public StorageStillReachableException(string path)
        : base(
            "error.storage.stillReachable",
            $"The storage folder '{path}' can be reached, so it was not replaced.",
            new Dictionary<string, object?> { ["path"] = path })
    {
    }
}
