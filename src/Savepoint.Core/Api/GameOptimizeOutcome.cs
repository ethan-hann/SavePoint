namespace Savepoint.Core.Api;

/// <summary>How storage optimization went for one game.</summary>
/// <param name="GameId">The optimized game.</param>
/// <param name="Folders">One outcome per folder repository the run touched for this game.</param>
/// <param name="BytesFreed">Bytes of storage the run freed, or null when the size could not be measured before and after.</param>
public sealed record GameOptimizeOutcome(string GameId, IReadOnlyList<FolderOutcome> Folders, long? BytesFreed);
