namespace Savepoint.Core.Api;

// One save the launch dialog can load; its id is the save point's history id (D15).
public sealed record SaveEntry(HistoryPointId Id, string Name, DateTimeOffset CapturedAt, bool IsMostRecent);
