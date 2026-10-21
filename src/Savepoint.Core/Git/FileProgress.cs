namespace Savepoint.Core.Git;

/// <summary>Progress of a capture or a checkout. Reports arrive on a background thread, at most a few times a second plus a final one.</summary>
/// <param name="FilesDone">Files handled so far; never decreases.</param>
/// <param name="FilesTotal">Files expected in all, never less than <paramref name="FilesDone"/>; null while still being counted.</param>
/// <param name="CurrentPath">The file being handled, relative to the work tree with forward slashes; null when git does not say.</param>
public readonly record struct FileProgress(int FilesDone, int? FilesTotal, string? CurrentPath);
