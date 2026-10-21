namespace Savepoint.Core.Errors;

public sealed class OperationCancelledByUserException : SavepointException
{
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
