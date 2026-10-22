namespace Savepoint.Core.Api;

/// <summary>What "Free up space" would delete for good that no profile holds any more: the recently removed snapshots.</summary>
/// <param name="RecentlyRemovedSnapshots">
/// How many recently removed snapshots it would delete, counted as <see cref="IHistoryApi.GetRecentlyRemovedAsync"/> lists them: for one
/// game, or across every game. Zero means nothing the player could still bring back is lost.
/// </param>
public sealed record FreeUpSpacePreview(int RecentlyRemovedSnapshots);
