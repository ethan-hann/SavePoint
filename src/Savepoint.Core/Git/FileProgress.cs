namespace Savepoint.Core.Git;

/// <summary>Progress of a capture or a checkout. Reports arrive on a background thread, at most a few times a second plus a final one.</summary>
/// <param name="FilesDone">Files handled so far; never decreases.</param>
/// <param name="FilesTotal">Files expected in all, never less than <paramref name="FilesDone"/>; null while still being counted.</param>
/// <param name="CurrentPath">The file being handled, relative to the work tree with forward slashes; null when git does not say.</param>
/// <param name="BytesDone">
/// Bytes written so far, or null when the operation does not count bytes. A capture measures the growth of the object store, so the
/// figure moves while one large file is being stored; it never decreases and stays under <paramref name="BytesTotal"/> until the last report.
/// </param>
/// <param name="BytesTotal">Bytes the operation expects to write in all (for a capture, the sizes of the files it stores), or null when not known.</param>
/// <param name="Finishing">
/// True once a capture has stored all it is going to and git is only wrapping up (closing its last pack, writing the index), which can
/// take minutes after a very large first capture and moves nothing else. It stays true until the final report, which clears it.
/// </param>
public readonly record struct FileProgress(int FilesDone, int? FilesTotal, string? CurrentPath, long? BytesDone = null, long? BytesTotal = null, bool Finishing = false);
