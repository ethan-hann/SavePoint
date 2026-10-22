namespace Savepoint.Core.Api;

/// <summary>
/// Whether "Take a snapshot now" can be used on a profile. A snapshot copies what the game folders hold now, which is the loaded profile's
/// files, so it only makes sense on the loaded profile, or on one that has no snapshot yet.
/// </summary>
public enum SnapshotNowAvailability
{
    /// <summary>Each folder holds this profile, or never saved one for it, so the snapshot adds to the profile's history.</summary>
    Available,

    /// <summary>
    /// The profile has no snapshot of any folder yet, so its first snapshot is whatever the folders hold now, which may be another
    /// profile's files. The caller should say so and ask first.
    /// </summary>
    FirstSnapshot,

    /// <summary>
    /// A folder holds another profile, or an older snapshot of this one. The snapshot is refused with <c>error.profile.notLoaded</c> or
    /// <c>error.launch.detachedCaptureNotSupported</c> before anything is written; the profile has to be loaded first.
    /// </summary>
    NotLoaded,
}
