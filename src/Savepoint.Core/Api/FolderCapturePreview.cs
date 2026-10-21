using Savepoint.Core.Catalog;

namespace Savepoint.Core.Api;

// Path is resolved; ModFileCount is always 0 for the saves folder.
public sealed record FolderCapturePreview(RepoRole Role, string Path, int FileCount, int ModFileCount, long Bytes);
