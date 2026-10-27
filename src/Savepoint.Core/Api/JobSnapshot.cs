using Savepoint.Core.Errors;

namespace Savepoint.Core.Api;

/// <summary>
/// Everything known about one job at one moment. Snapshots are immutable; each change makes a new one with a higher
/// <see cref="Revision"/>.
/// </summary>
/// <param name="JobId">The job's id, the one <see cref="JobStarted"/> returned.</param>
/// <param name="Kind">What the job does.</param>
/// <param name="GameId">The game the job is for, or null for a job that spans every game, such as a storage move.</param>
/// <param name="Profile">The profile the job works on (a snapshot or a launch), or null for a job that works on no one profile.</param>
/// <param name="Status">Where the job is in its life.</param>
/// <param name="Cancellable">
/// Whether <see cref="IJobsApi.CancelAsync"/> can stop the job. True for every kind but <see cref="JobKind.ClearData"/> (D13.18).
/// </param>
/// <param name="CancelRequested">
/// True once a cancel was asked for. The job is still stopping until <paramref name="Status"/> leaves <see cref="JobStatus.Running"/>.
/// </param>
/// <param name="Progress">The latest progress report; <see cref="JobProgress.None"/> before the first.</param>
/// <param name="RecentLines">The newest 200 lines of the job's details, oldest first: the files it handles and the steps it takes.</param>
/// <param name="LinesDropped">How many older lines have fallen off the front of <paramref name="RecentLines"/> since the job began, so a reader that holds an earlier snapshot can tell which lines are new.</param>
/// <param name="StartedAt">When the job started, as an absolute instant.</param>
/// <param name="FinishedAt">When the job ended, as an absolute instant; null while it is running.</param>
/// <param name="Result">
/// The payload by job kind. Null while the job runs, for a launch job, and when the work ended in an exception; a per-folder
/// result is kept even when every folder failed.
/// </param>
/// <param name="Error">
/// Null for <see cref="JobStatus.Succeeded"/> and <see cref="JobStatus.Partial"/>, where the per-folder errors are in the
/// result. Otherwise the error that failed or cancelled the job; for per-folder work it is the first folder's error.
/// </param>
/// <param name="Revision">Grows with every change, so a view model can drop an event older than the snapshot it holds.</param>
/// <param name="Restored">
/// True for a finished job listed from an earlier run, which keeps its summary but not its lines or result; false for every job of this run.
/// </param>
public sealed record JobSnapshot(
    Guid JobId,
    JobKind Kind,
    string? GameId,
    JobProfile? Profile,
    JobStatus Status,
    bool Cancellable,
    bool CancelRequested,
    JobProgress Progress,
    IReadOnlyList<JobLine> RecentLines,
    long LinesDropped,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    JobResult? Result,
    ErrorInfo? Error,
    long Revision,
    bool Restored = false);
