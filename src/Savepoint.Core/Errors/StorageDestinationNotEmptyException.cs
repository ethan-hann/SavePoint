namespace Savepoint.Core.Errors;

public sealed class StorageDestinationNotEmptyException : SavepointException
{
    public StorageDestinationNotEmptyException(string path)
        : base(
            "error.storageRelocation.destinationNotEmpty",
            $"The storage destination '{path}' is not empty.",
            new Dictionary<string, object?> { ["path"] = path })
    {
    }
}
