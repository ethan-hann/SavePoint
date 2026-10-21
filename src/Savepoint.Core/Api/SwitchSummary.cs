namespace Savepoint.Core.Api;

// EstimatedDuration is null when the service has no basis for a guess yet.
public sealed record SwitchSummary(int FilesChanged, int ModFilesChanged, long BytesChanged, TimeSpan? EstimatedDuration);
