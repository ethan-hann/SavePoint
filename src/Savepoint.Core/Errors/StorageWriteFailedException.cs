namespace Savepoint.Core.Errors;

/// <summary>
/// SavePoint could not write to a folder in its storage while it was making the repositories of a startup choice or rebuilding a damaged
/// index: the drive is read-only, or the account may not write there (<c>error.storage.writeFailed</c>). Nothing was changed, and it can be
/// tried again.
/// Args: <c>path</c>.
/// </summary>
public sealed class StorageWriteFailedException : SavepointException
{
    /// <summary>Creates the error.</summary>
    /// <param name="path">The game's folder in the storage that could not be written to.</param>
    /// <param name="devMessage">What went wrong, in English, for logs.</param>
    /// <param name="inner">What the file system said, if anything.</param>
    public StorageWriteFailedException(string path, string devMessage, Exception? inner = null)
        : base(
            "error.storage.writeFailed",
            devMessage,
            new Dictionary<string, object?> { ["path"] = path },
            inner)
    {
    }
}
