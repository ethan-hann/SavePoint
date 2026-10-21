using Savepoint.Core.Api;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Services.Jobs;

/// <summary>Turns what a job's work returned into the status and error its snapshot reports (D13.20).</summary>
public static class JobOutcome
{
    private static readonly HashSet<string> _cancelledMessageIds =
        [.. Enum.GetValues<CancelledOperation>().Select(op => ErrorInfo.From(new OperationCancelledByUserException(op, null)).MessageId)];

    /// <summary>Judges a finished job from its result.</summary>
    /// <param name="result">The result the work returned, or null for work with none, such as a launch.</param>
    /// <returns>
    /// The final status and the error to show. Per-folder work succeeds when every folder did, is partial when only some
    /// did (with no error), and otherwise failed or, when every failure is a cancellation, cancelled (with the first error).
    /// A clear-all-data result is partial when anything was left behind.
    /// </returns>
    public static (JobStatus Status, ErrorInfo? Error) Judge(JobResult? result)
    {
        if (result is ClearAllDataResult clear)
        {
            return clear.NotRemoved.Count == 0 ? (JobStatus.Succeeded, null) : (JobStatus.Partial, null);
        }

        IReadOnlyList<FolderOutcome> folders = result switch
        {
            CaptureResult capture => capture.Folders,
            OptimizeResult optimize => [.. optimize.Games.SelectMany(g => g.Folders)],
            _ => [],
        };
        var errors = folders.Where(f => !f.Ok).Select(f => f.Error!).ToList();
        if (errors.Count == 0)
        {
            return (JobStatus.Succeeded, null);
        }

        if (errors.Count < folders.Count)
        {
            return (JobStatus.Partial, null);
        }

        return errors.All(e => _cancelledMessageIds.Contains(e.MessageId)) ? (JobStatus.Cancelled, errors[0]) : (JobStatus.Failed, errors[0]);
    }

    /// <summary>The error for a job that ended because it was cancelled.</summary>
    /// <param name="kind">The kind of job; selects which cancelled message the user sees.</param>
    /// <param name="gameId">The game the job was for, or null for a job that spans every game.</param>
    /// <exception cref="InvalidOperationException">The kind cannot be cancelled.</exception>
    public static ErrorInfo CancelledError(JobKind kind, string? gameId)
    {
        var op = kind switch
        {
            JobKind.Launch => CancelledOperation.Launch,
            JobKind.Capture => CancelledOperation.Capture,
            JobKind.Compact => CancelledOperation.Compaction,
            JobKind.RelocateStorage => CancelledOperation.StorageRelocation,
            _ => throw new InvalidOperationException($"A {kind} job cannot be cancelled."),
        };
        return ErrorInfo.From(new OperationCancelledByUserException(op, gameId));
    }
}
