namespace Savepoint.Core.Api;

public sealed record CaptureResult(
    // The profile that received the point, which for a fork is the new one.
    string ProfileId,

    // Null when no folder was saved.
    HistoryPointId? PointId,
    IReadOnlyList<FolderOutcome> Folders) : JobResult;
