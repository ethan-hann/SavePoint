namespace Savepoint.Core.Api;

/// <summary>
/// The result of a clear-all-data job. It never touches the files in a game's install or save folders (D8). The job is
/// <see cref="JobStatus.Partial"/> when anything stayed behind.
/// </summary>
/// <param name="BytesFreed">Bytes of SavePoint's own data that were removed.</param>
/// <param name="NotRemoved">What stayed behind (a locked file, say), so the user can delete it by hand. Empty when all went.</param>
public sealed record ClearAllDataResult(long BytesFreed, IReadOnlyList<ClearFailure> NotRemoved) : JobResult;
