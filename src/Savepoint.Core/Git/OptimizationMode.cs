namespace Savepoint.Core.Git;

/// <summary>How far an optimization goes in tidying a repository's store.</summary>
public enum OptimizationMode
{
    /// <summary>
    /// Packs what was added since the last time and merges only small packs, so the large packs from earlier are never
    /// rewritten. Safe to run after every capture. Objects no profile reaches any more are packed along, not dropped.
    /// </summary>
    Routine,

    /// <summary>
    /// Drops everything no profile and no record of a removed snapshot reaches (a deleted profile's points): reflogs are
    /// expired, every reachable object is packed into one pack, and what is left over is pruned. Rewrites the whole store,
    /// so it needs free space about the size of the store and must not run while anything else writes to the repository. The
    /// snapshots a move forward or an amend took off a branch stay while their records do; deleting the records first
    /// (<see cref="GitEngine.ClearRemovedAsync"/>) is how "Free up space" lets them go.
    /// </summary>
    Reclaim,
}
