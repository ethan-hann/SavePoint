namespace Savepoint.Core.Api;

/// <summary>What switching one folder to the target would change.</summary>
/// <param name="FilesChanged">Files that would be added, replaced, or removed, mod files included.</param>
/// <param name="ModFilesChanged">How many of <paramref name="FilesChanged"/> are mod files.</param>
/// <param name="BytesChanged">Bytes that would be written: the size, at the target, of the files added or replaced. Removed files add nothing.</param>
/// <param name="EstimatedDuration">How long the switch is expected to take, or null when the service has no basis for a guess yet.</param>
/// <param name="BytesRemoved">Bytes the files that would be removed hold now; 0 when none are removed.</param>
public sealed record SwitchSummary(int FilesChanged, int ModFilesChanged, long BytesChanged, TimeSpan? EstimatedDuration, long BytesRemoved = 0);
