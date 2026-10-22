namespace Savepoint.Core.Api;

/// <summary>
/// A snapshot that left a profile and is kept until the player next frees up space. It belongs to no profile, so History, the
/// delete-profile warning, a profile's snapshot count and every launch ignore it.
/// </summary>
/// <param name="Id">
/// Names this snapshot; the same id goes into <see cref="IProfilesApi.BringBackAsync"/>. It is the id the snapshot had in History, which
/// the snapshot that replaced it never shares.
/// </param>
/// <param name="Kind">Which folders the snapshot recorded, as History shows it.</param>
/// <param name="Name">The snapshot's name, as it had in History.</param>
/// <param name="CapturedAt">When the snapshot was taken, as an absolute instant.</param>
/// <param name="Install">The install state current at that snapshot, also for one that recorded only saves.</param>
/// <param name="FromProfileId">The profile the snapshot left; null when that profile has been deleted since.</param>
/// <param name="FromProfileName">That profile's current name; null when <paramref name="FromProfileId"/> is.</param>
/// <param name="RemovedAt">When the snapshot left the profile.</param>
/// <param name="Reason">Why it left.</param>
public sealed record RemovedSnapshot(
    HistoryPointId Id,
    HistoryPointKind Kind,
    string Name,
    DateTimeOffset CapturedAt,
    InstallState Install,
    string? FromProfileId,
    string? FromProfileName,
    DateTimeOffset RemovedAt,
    RemovedSnapshotReason Reason);
