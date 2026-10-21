namespace Savepoint.Core.Api;

// Long work returns at once; progress and the result arrive through IJobsApi.
public sealed record JobStarted(Guid JobId);
