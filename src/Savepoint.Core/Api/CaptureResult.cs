namespace Savepoint.Core.Api;

/// <summary>The result of a capture job: which profile got the new history point and how each folder fared.</summary>
/// <param name="ProfileId">The profile that received the point, which for a fork is the new one.</param>
/// <param name="PointId">The history point the capture made, or null when no folder was saved.</param>
/// <param name="Folders">One outcome per folder the capture touched. A mix of successes and failures makes the job partial.</param>
public sealed record CaptureResult(
    string ProfileId,
    HistoryPointId? PointId,
    IReadOnlyList<FolderOutcome> Folders) : JobResult;
