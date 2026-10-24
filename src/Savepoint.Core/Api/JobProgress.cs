namespace Savepoint.Core.Api;

/// <summary>
/// How far a job has got. Each report replaces the previous one, so a null total means the bar is indeterminate and the
/// UI shows what it has.
/// </summary>
/// <param name="FilesDone">Files finished so far, or null when the job does not count files.</param>
/// <param name="FilesTotal">Files the job expects to handle in all, or null when not known yet.</param>
/// <param name="BytesDone">Bytes finished so far, or null when the job does not count bytes.</param>
/// <param name="BytesTotal">Bytes the job expects to handle in all, or null when not known yet.</param>
/// <param name="CurrentPath">The file being handled now, for the activity line, or null between files.</param>
/// <param name="Finishing">
/// True while the job has stored everything and is only wrapping up, so the counts no longer move; a screen says so instead of showing a
/// figure that looks stuck. Cleared by the final report.
/// </param>
public sealed record JobProgress(
    int? FilesDone = null,
    int? FilesTotal = null,
    long? BytesDone = null,
    long? BytesTotal = null,
    string? CurrentPath = null,
    bool Finishing = false)
{
    /// <summary>No progress to report yet; an indeterminate bar.</summary>
    public static JobProgress None { get; } = new();
}
