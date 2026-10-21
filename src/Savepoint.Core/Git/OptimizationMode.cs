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
    /// Drops everything no profile reaches (a deleted profile's points, points a move forward dropped): reflogs are
    /// expired, every reachable object is packed into one pack, and what is left over is pruned. Rewrites the whole store,
    /// so it needs free space about the size of the store and must not run while anything else writes to the repository.
    /// </summary>
    Reclaim,
}
