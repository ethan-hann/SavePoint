namespace Savepoint.Core.Git;

/// <summary>One recorded point.</summary>
/// <param name="Id">The full commit id.</param>
/// <param name="Subject">The point's name, as given to the capture.</param>
/// <param name="Date">When the point was recorded, with the offset it was recorded in.</param>
/// <param name="CaptureId">The <c>Savepoint-Capture</c> trailer; null for a commit that has none.</param>
public sealed record CommitInfo(string Id, string Subject, DateTimeOffset Date, string? CaptureId);
