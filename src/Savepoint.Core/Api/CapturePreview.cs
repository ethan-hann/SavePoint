namespace Savepoint.Core.Api;

// NewBytesEstimate is what a capture would add after reusing files SavePoint already stores.
public sealed record CapturePreview(string GameId, IReadOnlyList<FolderCapturePreview> Folders, long NewBytesEstimate);
