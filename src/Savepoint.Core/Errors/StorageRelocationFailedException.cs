namespace Savepoint.Core.Errors;

/// <summary>A file could not be copied to the new storage folder or did not match its source afterwards; the old storage is untouched.</summary>
public sealed class StorageRelocationFailedException : SavepointException
{
    /// <summary>Creates the error.</summary>
    /// <param name="path">The file at the destination that failed to copy or verify.</param>
    /// <param name="devMessage">What went wrong, in English, for logs.</param>
    /// <param name="inner">The underlying failure, if there is one.</param>
    public StorageRelocationFailedException(string path, string devMessage, Exception? inner)
        : base(
            "error.storageRelocation.copyFailed",
            devMessage,
            new Dictionary<string, object?> { ["path"] = path },
            inner)
    {
    }
}
