namespace Savepoint.Core.Errors;

/// <summary>
/// A job ended because the user cancelled it. The message id follows the operation, and a cancelled job is never reported as a
/// failure. Args: <c>op</c>, the <see cref="CancelledOperation"/> as a camelCase string, and <c>gameId</c>.
/// </summary>
public sealed class OperationCancelledByUserException : SavepointException
{
    /// <summary>Creates the error.</summary>
    /// <param name="op">What was cancelled; it picks the message id.</param>
    /// <param name="gameId">The game the job was for, or null for a job that spans every game.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="op"/> is not a defined operation.</exception>
    public OperationCancelledByUserException(CancelledOperation op, string? gameId)
        : base(
            MessageIdFor(op),
            $"The user canceled the {op} operation.",
            new Dictionary<string, object?> { ["op"] = op, ["gameId"] = gameId })
    {
    }

    // The launch id keeps the single-l spelling the v1 catalog used.
    private static string MessageIdFor(CancelledOperation op) => op switch
    {
        CancelledOperation.Capture => "error.capture.cancelled",
        CancelledOperation.Compaction => "error.compaction.cancelled",
        CancelledOperation.Launch => "error.launch.canceled",
        CancelledOperation.StorageRelocation => "error.storageRelocation.cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(op), op, "Unknown operation."),
    };
}
