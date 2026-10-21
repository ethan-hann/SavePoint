namespace Savepoint.Core.Api;

/// <summary>How much storage one game takes, and how full the storage volume is.</summary>
/// <param name="GameId">The game.</param>
/// <param name="BytesUsed">Bytes of SavePoint's own storage for this game.</param>
/// <param name="VolumeTotalBytes">Size, in bytes, of the drive that holds the storage folder.</param>
/// <param name="VolumeFreeBytes">Free space, in bytes, on that drive.</param>
public sealed record GameStorageUsage(string GameId, long BytesUsed, long VolumeTotalBytes, long VolumeFreeBytes);
