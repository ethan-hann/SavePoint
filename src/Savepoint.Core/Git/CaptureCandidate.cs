namespace Savepoint.Core.Git;

/// <summary>A file a capture would record.</summary>
/// <param name="Path">Relative to the work tree, with forward slashes.</param>
/// <param name="Bytes">The file's size on disk.</param>
public sealed record CaptureCandidate(string Path, long Bytes);
