namespace Savepoint.Core.Api;

/// <summary>
/// The result of a finished storage move. The settings point at the new folder, and SavePoint's own copies in the previous one
/// have been removed; the previous folder itself and anything else in it are left alone, and so is a copy that could not be removed.
/// </summary>
/// <param name="PreviousRoot">The storage folder before the move; it is not used any more.</param>
/// <param name="NewRoot">The storage folder now in use.</param>
/// <param name="BytesCopied">Bytes copied into the new folder.</param>
public sealed record StorageRelocationResult(string PreviousRoot, string NewRoot, long BytesCopied) : JobResult;
