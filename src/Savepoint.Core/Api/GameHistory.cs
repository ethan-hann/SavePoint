namespace Savepoint.Core.Api;

// Points are newest first; ProfileId echoes the filter the list was built with, null for every profile.
public sealed record GameHistory(string GameId, string? ProfileId, IReadOnlyList<HistoryPoint> Points);
