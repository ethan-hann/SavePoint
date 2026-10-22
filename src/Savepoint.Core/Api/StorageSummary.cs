namespace Savepoint.Core.Api;

/// <summary>SavePoint's storage across every game, for the Settings screen.</summary>
/// <param name="StorageRoot">The storage folder in use.</param>
/// <param name="BytesUsed">Bytes of SavePoint's own storage across every game.</param>
/// <param name="Games">Each game's share of <paramref name="BytesUsed"/>.</param>
/// <param name="VolumeTotalBytes">Size, in bytes, of the drive that holds the storage folder.</param>
/// <param name="VolumeFreeBytes">Free space, in bytes, on that drive.</param>
/// <param name="LastOptimizedAt">When the player last freed up space, or null if they never did; the cleanups SavePoint starts by itself do not count.</param>
public sealed record StorageSummary(
    string StorageRoot,
    long BytesUsed,
    IReadOnlyList<GameBytesUsed> Games,
    long VolumeTotalBytes,
    long VolumeFreeBytes,
    DateTimeOffset? LastOptimizedAt);
