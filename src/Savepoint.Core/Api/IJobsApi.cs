namespace Savepoint.Core.Api;

/// <summary>Follows and cancels the long-running work other calls start: launches, captures, optimization, moves, and clears.</summary>
/// <remarks>
/// The App subscribes to <see cref="JobChanged"/> first, then reads <see cref="List"/> or <see cref="Get"/>, and drops any event
/// whose <see cref="JobSnapshot.Revision"/> is not newer than the snapshot it already holds, so a late subscriber never loses a
/// result. See <see cref="OperationResult{T}"/> for the rules every member follows.
/// </remarks>
public interface IJobsApi
{
    /// <summary>
    /// Raised with the whole snapshot after every change: a job starts, reports progress, has a cancel requested, or ends. It is
    /// raised on a background thread, never the UI thread, so the App marshals it. Progress events may be coalesced; the start,
    /// the cancel request, and the end are never held back.
    /// </summary>
    event EventHandler<JobSnapshot>? JobChanged;

    /// <summary>
    /// The running jobs in start order, then the finished jobs in finish order: for each game its five most recently finished, and
    /// the five most recently finished that span every game, so a busy game never pushes another game's jobs out. Reads memory and
    /// cannot fail. Subscribe to <see cref="JobChanged"/> first, then call this.
    /// </summary>
    IReadOnlyList<JobSnapshot> List();

    /// <summary>Looks up a running job or a finished one that <see cref="List"/> still holds. Reads memory and cannot fail.</summary>
    /// <param name="jobId">The id <see cref="JobStarted"/> returned.</param>
    /// <returns>The job's latest snapshot, or null when the id is unknown or the job has been pruned from the finished list.</returns>
    JobSnapshot? Get(Guid jobId);

    /// <summary>
    /// Asks a job to stop. The job ends through its own work: its snapshot shows <see cref="JobSnapshot.CancelRequested"/> at
    /// once, and the job then ends, as <see cref="JobStatus.Cancelled"/> when it stopped early. The call is idempotent: an
    /// unknown, finished, already cancelled, or non-cancellable job is left alone and the call still succeeds.
    /// </summary>
    /// <param name="jobId">The job to stop.</param>
    Task<OperationResult<Unit>> CancelAsync(Guid jobId);
}
