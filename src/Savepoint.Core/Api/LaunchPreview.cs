namespace Savepoint.Core.Api;

public sealed record LaunchPreview(
    string GameId,

    // The profile the target resolves to, also for a Point target.
    string ProfileId,
    LaunchTarget Target,
    FolderLaunchState Install,
    FolderLaunchState? External,
    InstallState CurrentInstall,
    InstallState TargetInstall,

    // The target profile's save points, newest first.
    IReadOnlyList<SaveEntry> AvailableSaves,
    HistoryPointId? SelectedSaveId,

    // Nothing to switch and nothing unsaved, so the game starts without the dialog.
    bool ShowBanner,

    // Only the saves folder would change, which the "don't ask again" setting may skip.
    bool OnlySaveDiffers,

    // Not the profile's latest point, so a capture after playing follows D14.
    bool TargetIsOlderPoint);
