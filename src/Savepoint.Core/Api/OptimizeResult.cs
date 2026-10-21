namespace Savepoint.Core.Api;

/// <summary>The result of a storage optimization job.</summary>
/// <param name="Games">One entry per optimized game; a run for a single game has one entry.</param>
public sealed record OptimizeResult(IReadOnlyList<GameOptimizeOutcome> Games) : JobResult;
