using Savepoint.Core.Errors;

namespace Savepoint.Core.Api;

public sealed record StorageRelocationPreview(
    string CurrentRoot,
    string NewRoot,
    IReadOnlyList<GameBytesUsed> Games,
    long BytesToCopy,
    int FileCount,
    long DestinationFreeBytes,

    // The error the move would fail with (not empty, unsafe, too small, a game running); null when it can go ahead.
    ErrorInfo? BlockedBy);
