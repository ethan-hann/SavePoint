namespace Savepoint.Core.Errors;

/// <summary>
/// The volume an operation writes to has too little free space (<c>error.diskSpace.low</c>). The volume checked is the one written:
/// the storage volume for captures and optimization, the game folder's volume for switches (D13.16). Args: <c>path</c>,
/// <c>requiredBytes</c>, <c>freeBytes</c>.
/// </summary>
public sealed class DiskSpaceException : SavepointException
{
    /// <summary>Creates the error.</summary>
    /// <param name="path">A folder on the volume that was checked.</param>
    /// <param name="requiredBytes">Bytes the operation needs.</param>
    /// <param name="freeBytes">Bytes free on the volume when it was checked.</param>
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
