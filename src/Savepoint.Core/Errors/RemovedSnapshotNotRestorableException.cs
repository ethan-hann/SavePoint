namespace Savepoint.Core.Errors;

/// <summary>
/// A recently removed snapshot cannot become a profile because no game version and mods can be found for it: it recorded only saves,
/// and the profile it came from, which held the install it was played with, is gone (<c>error.removed.notRestorable</c>). A profile
/// made from it could not start. Args: <c>snapshotId</c>. Nothing was changed.
/// </summary>
public sealed class RemovedSnapshotNotRestorableException : SavepointException
{
    /// <summary>Creates the error.</summary>
    /// <param name="snapshotId">The snapshot that has no install to go with it.</param>
    public RemovedSnapshotNotRestorableException(string snapshotId)
        : base(
            "error.removed.notRestorable",
            $"The recently removed snapshot '{snapshotId}' recorded no game version and mods, and none can be found for it.",
            new Dictionary<string, object?> { ["snapshotId"] = snapshotId })
    {
    }
}
