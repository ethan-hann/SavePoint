namespace Savepoint.Core.Errors;

/// <summary>
/// A snapshot was asked for by id but the game's recently removed snapshots do not hold it, because the player freed up space since
/// the list was read or because it was brought back already (<c>error.removed.notFound</c>). Args: <c>snapshotId</c>. Nothing was changed.
/// </summary>
public sealed class RemovedSnapshotNotFoundException : SavepointException
{
    /// <summary>Creates the error.</summary>
    /// <param name="snapshotId">The id that matched no recently removed snapshot.</param>
    public RemovedSnapshotNotFoundException(string snapshotId)
        : base(
            "error.removed.notFound",
            $"The game has no recently removed snapshot '{snapshotId}'.",
            new Dictionary<string, object?> { ["snapshotId"] = snapshotId })
    {
    }
}
