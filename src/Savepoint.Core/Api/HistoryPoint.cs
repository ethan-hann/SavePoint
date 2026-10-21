namespace Savepoint.Core.Api;

public sealed record HistoryPoint(
    HistoryPointId Id,
    HistoryPointKind Kind,
    string Name,
    DateTimeOffset CapturedAt,
    string ProfileId,
    string ProfileName,

    // The install state current at that point, also for a save-only point.
    InstallState Install,

    // Against the profile's previous install point; null for its first one and for save-only points.
    int? ModFilesChanged,
    long? Bytes,

    // The install folder holds this point now.
    bool InUse,

    // The saves folder holds this point now.
    bool LoadedNow);
