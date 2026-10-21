namespace Savepoint.Core.Api;

/// <summary>What switching one folder to the target would change.</summary>
/// <param name="FilesChanged">Files that would be added, replaced, or removed, mod files included.</param>
/// <param name="ModFilesChanged">How many of <paramref name="FilesChanged"/> are mod files.</param>
/// <param name="BytesChanged">Bytes that would be written or removed.</param>
/// <param name="EstimatedDuration">How long the switch is expected to take, or null when the service has no basis for a guess yet.</param>
public sealed record SwitchSummary(int FilesChanged, int ModFilesChanged, long BytesChanged, TimeSpan? EstimatedDuration);
