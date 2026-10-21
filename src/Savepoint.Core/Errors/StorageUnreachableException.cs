namespace Savepoint.Core.Errors;

/// <summary>The folder where SavePoint keeps its history cannot be reached or created (an unplugged drive, a permission problem).</summary>
public sealed class StorageUnreachableException : SavepointException
{
    /// <summary>Creates the error.</summary>
    /// <param name="path">The storage folder.</param>
    /// <param name="inner">What the file system said, if anything.</param>
    public StorageUnreachableException(string path, Exception? inner)
        : base(
            "error.storage.unreachable",
            $"The storage folder '{path}' cannot be reached.",
            new Dictionary<string, object?> { ["path"] = path },
            inner)
    {
    }
}
