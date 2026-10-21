using Savepoint.Core.Catalog;

namespace Savepoint.Core.Api;

public sealed record ProfileRowStats(
    string ProfileId,

    // Both folders hold this profile's latest points with nothing unsaved.
    bool IsActive,

    // Folders on this profile with changes not yet saved.
    IReadOnlyList<RepoRole> UnsavedChanges,

    // Null when the game has no saves folder.
    int? SaveCount,

    // Null when the game names no mod folders or the profile has no install point yet.
    int? ModFileCount,
    string? DetectedVersion,
    string? VersionOverride,
    DateTimeOffset? LastPlayedAt,
    bool IsRunning);
