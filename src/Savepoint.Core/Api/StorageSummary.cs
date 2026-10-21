namespace Savepoint.Core.Api;

public sealed record StorageSummary(
    string StorageRoot,

    // SavePoint's own storage across every game.
    long BytesUsed,
    IReadOnlyList<GameBytesUsed> Games,
    long VolumeTotalBytes,
    long VolumeFreeBytes,
    DateTimeOffset? LastOptimizedAt);
