using Savepoint.Core.Catalog;

namespace Savepoint.Core.Api;

/// <summary>What a capture would save from one folder.</summary>
/// <param name="Role">Which folder.</param>
/// <param name="Path">The folder's resolved path on this machine.</param>
/// <param name="FileCount">Files a capture would save from the folder, ignored files left out.</param>
/// <param name="ModFileCount">How many of those are mod files; always 0 for the saves folder.</param>
/// <param name="Bytes">Total size of those files, in bytes.</param>
public sealed record FolderCapturePreview(RepoRole Role, string Path, int FileCount, int ModFileCount, long Bytes);
