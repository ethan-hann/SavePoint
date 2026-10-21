namespace Savepoint.Core.Api;

// Payload of a finished job; the concrete type follows the job kind (a launch job has none).
public abstract record JobResult;
