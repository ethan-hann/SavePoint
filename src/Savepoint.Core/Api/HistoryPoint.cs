namespace Savepoint.Core.Api;

/// <summary>One capture event in a game's history: the unit the History screen lists and "Go back and play" restores.</summary>
/// <param name="Id">Names this point; the same id goes into <see cref="LaunchTarget.Point"/> to go back to it.</param>
/// <param name="Kind">Which folders this capture changed.</param>
/// <param name="Name">The point's name, as the user typed it when saving.</param>
/// <param name="CapturedAt">When the capture happened, as an absolute instant. History sorts by this, never by text (D13.7).</param>
/// <param name="ProfileId">The profile the point belongs to.</param>
/// <param name="ProfileName">That profile's name.</param>
/// <param name="Install">The install state current at that point, also for a save-only point.</param>
/// <param name="ModFilesChanged">
/// How many mod files changed against the profile's previous install point; null for its first install point and for save-only
/// points.
/// </param>
/// <param name="Bytes">
/// The total size, in bytes, of the files the point recorded for the install folder: the whole install as it was then, not only
/// what changed. Null for a save-only point, which recorded no install of its own, and where SavePoint cannot tell.
/// </param>
/// <param name="InUse">True when the install folder holds this point now.</param>
/// <param name="LoadedNow">True when the saves folder holds this point now.</param>
public sealed record HistoryPoint(
    HistoryPointId Id,
    HistoryPointKind Kind,
    string Name,
    DateTimeOffset CapturedAt,
    string ProfileId,
    string ProfileName,
    InstallState Install,
    int? ModFilesChanged,
    long? Bytes,
    bool InUse,
    bool LoadedNow);
