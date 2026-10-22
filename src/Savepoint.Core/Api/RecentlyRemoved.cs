namespace Savepoint.Core.Api;

/// <summary>A game's recently removed snapshots.</summary>
/// <param name="GameId">The game.</param>
/// <param name="Snapshots">The snapshots, the most recently removed first. Empty when none was removed or the player freed up space since.</param>
public sealed record RecentlyRemoved(string GameId, IReadOnlyList<RemovedSnapshot> Snapshots);
