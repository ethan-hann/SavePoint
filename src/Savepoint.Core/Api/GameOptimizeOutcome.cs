namespace Savepoint.Core.Api;

// BytesFreed is null when the size could not be measured before and after.
public sealed record GameOptimizeOutcome(string GameId, IReadOnlyList<FolderOutcome> Folders, long? BytesFreed);
