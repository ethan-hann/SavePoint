namespace Savepoint.Core.Errors;

public sealed class DiskSpaceException : SavepointException
{
    public DiskSpaceException(string path, long requiredBytes, long freeBytes)
        : base(
            "error.diskSpace.low",
            $"Not enough free space at '{path}': {requiredBytes} bytes needed, {freeBytes} free.",
            new Dictionary<string, object?>
            {
                ["path"] = path,
                ["requiredBytes"] = requiredBytes,
                ["freeBytes"] = freeBytes,
            })
    {
    }
}
