namespace Savepoint.Core.Api;

// Each report replaces the previous one; a null total means the bar is indeterminate.
public sealed record JobProgress(
    int? FilesDone = null,
    int? FilesTotal = null,
    long? BytesDone = null,
    long? BytesTotal = null,
    string? CurrentPath = null)
{
    public static JobProgress None { get; } = new();
}
