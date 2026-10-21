namespace Savepoint.Core.Api;

/// <summary>A game's history, optionally narrowed to one profile.</summary>
/// <param name="GameId">The game.</param>
/// <param name="ProfileId">The profile filter the list was built with; null when it holds every profile's points.</param>
/// <param name="Points">The points, newest first by <see cref="HistoryPoint.CapturedAt"/>. Empty when the game has none yet.</param>
public sealed record GameHistory(string GameId, string? ProfileId, IReadOnlyList<HistoryPoint> Points);
