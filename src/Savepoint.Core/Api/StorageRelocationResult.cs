namespace Savepoint.Core.Api;

// The previous folder is left in place; only the settings point at the new one.
public sealed record StorageRelocationResult(string PreviousRoot, string NewRoot, long BytesCopied) : JobResult;
