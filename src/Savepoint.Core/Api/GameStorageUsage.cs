namespace Savepoint.Core.Api;

// BytesUsed is SavePoint's own storage for this game; the volume figures are for the storage folder's drive.
public sealed record GameStorageUsage(string GameId, long BytesUsed, long VolumeTotalBytes, long VolumeFreeBytes);
