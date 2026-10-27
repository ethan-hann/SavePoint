using Savepoint.Core.Catalog;

namespace Savepoint.Core.Api;

/// <summary>What starting a game for a target would do, read before anything changes. It checks every folder for unsaved changes (D13.8).</summary>
/// <param name="GameId">The game.</param>
/// <param name="ProfileId">The profile the target resolves to, also for a <see cref="LaunchTarget.Point"/> target.</param>
/// <param name="Target">The target the preview was built for.</param>
/// <param name="Folders">
/// The state of each folder the game has, in the order a switch writes them: the install folder always, then the mods folder and the saves
/// folder when the game has them. Each role appears once.
/// </param>
/// <param name="CurrentInstall">What the install folder holds now, with the mods folder's files counted into the mod-file count.</param>
/// <param name="TargetInstall">What it would hold after the switch.</param>
/// <param name="AvailableSaves">The target profile's save points, newest first; empty when it has none.</param>
/// <param name="SelectedSaveId">The save that would load, one of <paramref name="AvailableSaves"/>; null when there is none.</param>
/// <param name="ShowBanner">True when there is nothing to switch and nothing unsaved, so the game starts without the dialog.</param>
/// <param name="OnlySaveDiffers">True when only the saves folder would change (the install and mods folders already match), which the "don't ask again" setting may skip.</param>
/// <param name="TargetIsOlderPoint">
/// True when the target is not the profile's latest point, so a capture after playing follows the fork-or-move-forward choice (D14).
/// </param>
public sealed record LaunchPreview(
    string GameId,
    string ProfileId,
    LaunchTarget Target,
    IReadOnlyList<FolderLaunchState> Folders,
    InstallState CurrentInstall,
    InstallState TargetInstall,
    IReadOnlyList<SaveEntry> AvailableSaves,
    HistoryPointId? SelectedSaveId,
    bool ShowBanner,
    bool OnlySaveDiffers,
    bool TargetIsOlderPoint)
{
    /// <summary>The install folder's state, which every game has.</summary>
    public FolderLaunchState Install => Folder(RepoRole.Install) ?? throw new InvalidOperationException("A launch preview always carries the install folder.");

    /// <summary>One folder's state.</summary>
    /// <param name="role">Which folder.</param>
    /// <returns>The state, or null when the game has no folder of that role.</returns>
    public FolderLaunchState? Folder(RepoRole role) => Folders.FirstOrDefault(folder => folder.Role == role);
}
