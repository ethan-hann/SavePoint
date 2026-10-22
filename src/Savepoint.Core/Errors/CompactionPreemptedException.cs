namespace Savepoint.Core.Errors;

/// <summary>
/// Optimizing storage was stopped so something else on the same game could go first (D13.17). Unlike
/// <see cref="OperationCancelledByUserException"/> nobody asked for it, so the message says the work stopped on its own and that
/// nothing was lost. Nothing starts it again by itself, so the message points at Free up space to finish the job.
/// </summary>
public sealed class CompactionPreemptedException : SavepointException
{
    /// <summary>Creates the error.</summary>
    /// <param name="gameId">The game whose storage was being optimized, or null when the job spans every game.</param>
    public CompactionPreemptedException(string? gameId)
        : base(
            "error.compaction.preempted",
            $"Optimizing storage was preempted{(gameId is null ? string.Empty : $" for game '{gameId}'")}.",
            new Dictionary<string, object?> { ["gameId"] = gameId })
    {
    }
}
