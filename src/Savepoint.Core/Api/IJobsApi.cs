namespace Savepoint.Core.Api;

public interface IJobsApi
{
    // Raised on a background thread with the whole snapshot after each change; the App marshals it to the UI thread.
    event EventHandler<JobSnapshot>? JobChanged;

    // Running jobs in start order, then the five most recently finished in finish order; subscribe first, then call this.
    IReadOnlyList<JobSnapshot> List();

    JobSnapshot? Get(Guid jobId);

    // Idempotent: an unknown, finished, or non-cancellable job is left alone and the call still succeeds.
    Task<OperationResult<Unit>> CancelAsync(Guid jobId);
}
