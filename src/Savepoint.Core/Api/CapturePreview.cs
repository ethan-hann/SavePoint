namespace Savepoint.Core.Api;

/// <summary>What a capture would save now. Reading it writes nothing into the game folders (D13.1).</summary>
/// <param name="GameId">The game.</param>
/// <param name="Folders">One entry per configured folder.</param>
/// <param name="NewBytesEstimate">Bytes a capture would add to SavePoint's storage, after reusing files it already stores.</param>
public sealed record CapturePreview(string GameId, IReadOnlyList<FolderCapturePreview> Folders, long NewBytesEstimate);
