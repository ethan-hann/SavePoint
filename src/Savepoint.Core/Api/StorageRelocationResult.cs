namespace Savepoint.Core.Api;

/// <summary>
/// The result of a finished storage move. The settings point at the new folder, and SavePoint's own copies in the previous one
/// have been removed; the previous folder itself and anything else in it are left alone, and so is a copy that could not be
/// removed. The job is <see cref="JobStatus.Partial"/> when any copy was left, since the move itself worked.
/// </summary>
/// <param name="PreviousRoot">The storage folder before the move; it is not used any more.</param>
/// <param name="NewRoot">The storage folder now in use.</param>
/// <param name="BytesCopied">Bytes copied into the new folder.</param>
/// <param name="NotRemoved">The old copies that stayed behind (a locked file, say), so the user can delete them by hand. Empty when all went.</param>
public sealed record StorageRelocationResult(string PreviousRoot, string NewRoot, long BytesCopied, IReadOnlyList<ClearFailure> NotRemoved) : JobResult;
