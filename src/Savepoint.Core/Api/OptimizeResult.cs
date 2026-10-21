namespace Savepoint.Core.Api;

// One entry per optimized game; a single-game run has one entry.
public sealed record OptimizeResult(IReadOnlyList<GameOptimizeOutcome> Games) : JobResult;
