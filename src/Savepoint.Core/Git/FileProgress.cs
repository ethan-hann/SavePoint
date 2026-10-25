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
/// take minutes after a very large first capture and moves nothing else. It stays true until the last report, which clears it, whether the capture completes, fails or is stopped.
/// </param>
/// <param name="NewPaths">
/// Every file name git printed since the previous report, oldest first, so a reader that wants each name gets it once whatever the pace of the reports;
/// null when there are none. <paramref name="CurrentPath"/> is the last of them.
/// </param>
/// <param name="Copying">
/// A plain-words detail for the stretch where git prints no name, set on at most one report in a while; null otherwise.
/// </param>
public readonly record struct FileProgress(
    int FilesDone,
    int? FilesTotal,
    string? CurrentPath,
    long? BytesDone = null,
    long? BytesTotal = null,
    bool Finishing = false,
    IReadOnlyList<string>? NewPaths = null,
    CopyDetail? Copying = null);

/// <summary>What a capture is busy with while git prints nothing: one large file, or the folder as a whole.</summary>
/// <param name="Path">The one large file being stored, relative to the work tree with forward slashes; null when the detail is about the folder.</param>
/// <param name="BytesDone">Bytes of the file (or of the folder) stored so far; below <paramref name="BytesTotal"/> until the work is done.</param>
/// <param name="BytesTotal">Size of the file, or of everything the folder's capture stores.</param>
/// <param name="Starting">True for the first detail of one large file, sent when its storing begins.</param>
public readonly record struct CopyDetail(string? Path, long BytesDone, long BytesTotal, bool Starting = false);
