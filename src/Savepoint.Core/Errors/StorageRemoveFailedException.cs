namespace Savepoint.Core.Errors;

/// <summary>
/// Something in SavePoint's storage could not be deleted for a reason closing a program does not fix: access is denied, the file
/// is read-only, or the volume is (<c>error.storage.removeFailed</c>). A file another program holds open is
/// <c>error.gitOperation.checkoutFailed</c> instead. Args: <c>path</c>.
/// </summary>
public sealed class StorageRemoveFailedException : SavepointException
{
    /// <summary>Creates the error.</summary>
    /// <param name="path">The item that could not be removed.</param>
    /// <param name="devMessage">What went wrong, in English, for logs.</param>
    /// <param name="inner">What the file system said, if anything.</param>
    public StorageRemoveFailedException(string path, string devMessage, Exception? inner = null)
        : base(
            "error.storage.removeFailed",
            devMessage,
            new Dictionary<string, object?> { ["path"] = path },
            inner)
    {
    }
}
