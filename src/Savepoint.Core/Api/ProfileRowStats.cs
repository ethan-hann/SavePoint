using Savepoint.Core.Catalog;

namespace Savepoint.Core.Api;

/// <summary>What one row of the profile table shows beyond the profile itself.</summary>
/// <param name="ProfileId">The profile.</param>
/// <param name="IsActive">True when both folders hold this profile's latest points with nothing unsaved.</param>
/// <param name="UnsavedChanges">The folders on this profile that have changes not yet saved; empty for none.</param>
/// <param name="SnapshotCount">
/// How many snapshots the profile owns, whichever folders each took: the number History lists for the profile, so a snapshot both
/// profiles can reach counts only for the first profile that reaches it. Null when the snapshots were not counted, as for a row drawn
/// before its figures arrive.
/// </param>
/// <param name="ModFileCount">
/// How many mod files the profile's install holds; null when the game names no mod folders or the profile has no install point yet.
/// </param>
/// <param name="DetectedVersion">The version read from the game's program at the profile's latest install point; null when it cannot be read.</param>
/// <param name="VersionOverride">The profile's own version override (D13.6), or null.</param>
/// <param name="LastPlayedAt">When the profile was last launched, as an absolute instant; null if it never was.</param>
/// <param name="IsRunning">True when the game is running on this profile.</param>
/// <param name="SnapshotNow">
/// Whether "Take a snapshot now" works on this profile: on the loaded profile, and on a profile with no snapshot yet (whose first snapshot is
/// what the folders hold now); not on one the folders do not hold. Decided by the rule the Api's refusal uses, so the two never disagree.
/// </param>
public sealed record ProfileRowStats(
    string ProfileId,
    bool IsActive,
    IReadOnlyList<RepoRole> UnsavedChanges,
    int? SnapshotCount,
    int? ModFileCount,
    string? DetectedVersion,
    string? VersionOverride,
    DateTimeOffset? LastPlayedAt,
    bool IsRunning,
    SnapshotNowAvailability SnapshotNow = SnapshotNowAvailability.NotLoaded);
