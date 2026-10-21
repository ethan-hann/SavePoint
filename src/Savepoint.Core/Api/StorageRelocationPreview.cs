using Savepoint.Core.Errors;

namespace Savepoint.Core.Api;

/// <summary>
/// What moving the storage folder would copy, so the confirmation is exact. Once everything is copied and checked, SavePoint
/// removes its own copies from the previous folder.
/// </summary>
/// <param name="CurrentRoot">The storage folder now in use.</param>
/// <param name="NewRoot">The folder the data would move to.</param>
/// <param name="Games">Bytes of storage each game would bring along.</param>
/// <param name="BytesToCopy">Bytes the move would copy in all.</param>
/// <param name="FileCount">Files the move would copy in all.</param>
/// <param name="DestinationFreeBytes">Free space, in bytes, on the volume that holds the new folder.</param>
/// <param name="BlockedBy">
/// The error the move would fail with (a folder that is not empty or not safe, too little space, a running game), or null when it
/// can go ahead.
/// </param>
public sealed record StorageRelocationPreview(
    string CurrentRoot,
    string NewRoot,
    IReadOnlyList<GameBytesUsed> Games,
    long BytesToCopy,
    int FileCount,
    long DestinationFreeBytes,
    ErrorInfo? BlockedBy);
