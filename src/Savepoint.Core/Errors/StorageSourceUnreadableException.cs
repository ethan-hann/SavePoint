namespace Savepoint.Core.Errors;

/// <summary>
/// Something in the current storage cannot be moved: a folder or file that cannot be read, or a game folder that is a link
/// (<c>error.storageRelocation.sourceUnreadable</c>). Nothing was copied and nothing changed. Args: <c>path</c>, the entry in the
/// current storage.
/// </summary>
public sealed class StorageSourceUnreadableException : SavepointException
{
    /// <summary>Creates the error.</summary>
    /// <param name="path">The entry in the current storage that would not be carried over.</param>
    /// <param name="devMessage">What went wrong, in English, for logs.</param>
    /// <param name="inner">The underlying failure, if there is one.</param>
    public StorageSourceUnreadableException(string path, string devMessage, Exception? inner)
        : base(
            "error.storageRelocation.sourceUnreadable",
            devMessage,
            new Dictionary<string, object?> { ["path"] = path },
            inner)
    {
    }
}
