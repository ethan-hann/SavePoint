namespace Savepoint.Core.Git;

/// <summary>One recorded point.</summary>
/// <param name="Id">The full commit id.</param>
/// <param name="Subject">The point's name, as given to the capture.</param>
/// <param name="Date">When the point was recorded, with the offset it was recorded in.</param>
/// <param name="CaptureId">The <c>Savepoint-Capture</c> trailer; null for a commit that has none.</param>
/// <param name="CapturedAt">
/// The <c>Savepoint-Captured-At</c> trailer: when the capture event began, to the millisecond, the same for every commit the event
/// wrote. Null for a commit from before the trailer existed, which falls back to <paramref name="Date"/> (whole seconds).
/// </param>
public sealed record CommitInfo(string Id, string Subject, DateTimeOffset Date, string? CaptureId, DateTimeOffset? CapturedAt = null)
{
    /// <summary>When the point happened: the capture time to the millisecond when the commit has one, otherwise its date.</summary>
    public DateTimeOffset PointTime => CapturedAt ?? Date;
}
