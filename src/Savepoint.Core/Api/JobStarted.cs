namespace Savepoint.Core.Api;

/// <summary>What a call that starts long work returns at once. Progress and the result arrive through <see cref="IJobsApi"/>.</summary>
/// <param name="JobId">The new job. Look it up with <see cref="IJobsApi.Get"/> or follow it through <see cref="IJobsApi.JobChanged"/>.</param>
public sealed record JobStarted(Guid JobId);
