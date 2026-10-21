namespace Savepoint.Core.Errors;

/// <summary>The kinds of work a user can cancel; each has its own cancelled message id.</summary>
public enum CancelledOperation
{
    /// <summary>A capture, including the capture after play (<c>error.capture.cancelled</c>).</summary>
    Capture,

    /// <summary>Storage optimization (<c>error.compaction.cancelled</c>).</summary>
    Compaction,

    /// <summary>The switch and start of a game (<c>error.launch.canceled</c>, spelled with one l as in v1).</summary>
    Launch,

    /// <summary>Moving the storage folder (<c>error.storageRelocation.cancelled</c>).</summary>
    StorageRelocation,
}
