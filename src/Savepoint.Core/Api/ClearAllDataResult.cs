namespace Savepoint.Core.Api;

// NotRemoved lists what stayed behind (a locked file, say), so the user can delete it by hand.
public sealed record ClearAllDataResult(long BytesFreed, IReadOnlyList<ClearFailure> NotRemoved) : JobResult;
