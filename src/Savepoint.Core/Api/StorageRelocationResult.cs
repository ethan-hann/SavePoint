namespace Savepoint.Core.Api;

/// <summary>
/// The result of a finished storage move. The previous folder is left in place; only the settings point at the new one.
/// </summary>
/// <param name="PreviousRoot">The storage folder before the move, which still holds the old copy.</param>
/// <param name="NewRoot">The storage folder now in use.</param>
/// <param name="BytesCopied">Bytes copied into the new folder.</param>
public sealed record StorageRelocationResult(string PreviousRoot, string NewRoot, long BytesCopied) : JobResult;
