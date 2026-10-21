namespace Savepoint.Core.Api;

/// <summary>One save the launch dialog can load.</summary>
/// <param name="Id">The save point's history id (D15); it goes into <see cref="LaunchTarget.Save"/>.</param>
/// <param name="Name">The point's name, as the user typed it when saving.</param>
/// <param name="CapturedAt">When the save was captured, as an absolute instant.</param>
/// <param name="IsMostRecent">True for the profile's newest save, so the picker can mark it.</param>
public sealed record SaveEntry(HistoryPointId Id, string Name, DateTimeOffset CapturedAt, bool IsMostRecent);
