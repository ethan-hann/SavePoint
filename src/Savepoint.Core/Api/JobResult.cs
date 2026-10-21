namespace Savepoint.Core.Api;

/// <summary>
/// The payload of a finished job. The concrete type follows the job kind: <see cref="CaptureResult"/>,
/// <see cref="OptimizeResult"/>, <see cref="StorageRelocationResult"/>, or <see cref="ClearAllDataResult"/>. A launch job has none.
/// </summary>
public abstract record JobResult;
