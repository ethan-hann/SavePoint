namespace Savepoint.Core.Api;

/// <summary>Why a snapshot is no longer part of a profile.</summary>
public enum RemovedSnapshotReason
{
    /// <summary>
    /// The player chose "Continue the profile from this session" after playing from an older snapshot, so the profile moved back to
    /// that snapshot and the newer ones left it.
    /// </summary>
    ContinuedFromSession,

    /// <summary>
    /// The player chose "Replace the snapshot with this session", so the profile's latest snapshot was rewritten and what it held
    /// before left the profile.
    /// </summary>
    ReplacedWithSession,
}
