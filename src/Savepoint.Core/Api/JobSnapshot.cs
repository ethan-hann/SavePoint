using Savepoint.Core.Errors;

namespace Savepoint.Core.Api;

public sealed record JobSnapshot(
    Guid JobId,
    JobKind Kind,

    // Null for jobs that span every game, such as a storage move.
    string? GameId,
    JobStatus Status,
    bool Cancellable,
    bool CancelRequested,
    JobProgress Progress,

    // The newest 200 progress paths, oldest first.
    IReadOnlyList<string> RecentLines,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    JobResult? Result,

    // Set when the job failed or was cancelled; for per-folder work it is the first folder's error.
    ErrorInfo? Error,

    // Grows with every change, so a view model can drop an event older than the snapshot it holds.
    long Revision);
