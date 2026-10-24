namespace Savepoint.Core.Api;

/// <summary>
/// The catalog ids of the steps jobs write into their details (see <see cref="JobLine.Step"/>). Each constant's remarks list the
/// arguments its message takes. A folder is named by its <see cref="Savepoint.Core.Catalog.RepoRole"/> as a camelCase string
/// (<c>install</c> or <c>external</c>), a byte count is named <c>bytes</c>, and a count that a plural branches on is a number.
/// </summary>
public static class JobSteps
{
    /// <summary>A snapshot of a profile begins. Arguments: <c>profile</c>, <c>snapshot</c>.</summary>
    public const string CaptureStart = "activity.step.capture.start";

    /// <summary>A snapshot starts copying one folder. Arguments: <c>folder</c>, <c>path</c>.</summary>
    public const string CaptureFolder = "activity.step.capture.folder";

    /// <summary>A folder is copied. Arguments: <c>folder</c>, <c>files</c>, the files found new, changed or removed since the last snapshot, which may be none.</summary>
    public const string CaptureFolderDone = "activity.step.capture.folderDone";

    /// <summary>A folder is not part of the snapshot because it was played from another profile's snapshot. Arguments: <c>folder</c>.</summary>
    public const string CaptureFolderNotIncluded = "activity.step.capture.folderNotIncluded";

    /// <summary>A folder could not be copied. Arguments: <c>folder</c>.</summary>
    public const string CaptureFolderFailed = "activity.step.capture.folderFailed";

    /// <summary>A save asked to replace a snapshot added the session to a folder as a new snapshot instead, because the snapshot was not the newest there. Arguments: <c>folder</c>.</summary>
    public const string CaptureFolderKeptAsNew = "activity.step.capture.folderKeptAsNew";

    /// <summary>
    /// A folder is switched to a profile or snapshot. Arguments: <c>folder</c>, <c>profile</c>, <c>older</c> (<c>yes</c> when the target is
    /// an older snapshot, <c>no</c> otherwise), <c>counted</c> (<c>yes</c> when the counts are known, <c>no</c> otherwise), <c>written</c>,
    /// the files the switch writes, and <c>removed</c>, the files it removes; both are 0 when they are not counted.
    /// </summary>
    public const string LaunchSwitchFolder = "activity.step.launch.switchFolder";

    /// <summary>A folder already holds what the launch wants, so nothing is switched. Arguments: <c>folder</c>, <c>profile</c>.</summary>
    public const string LaunchFolderInPlace = "activity.step.launch.folderInPlace";

    /// <summary>A folder is left as it is, because the snapshot being gone back to holds nothing of it. Arguments: <c>folder</c>.</summary>
    public const string LaunchFolderNotIncluded = "activity.step.launch.folderNotIncluded";

    /// <summary>The game is about to start. Arguments: <c>game</c>.</summary>
    public const string LaunchStarting = "activity.step.launch.starting";

    /// <summary>Freeing up space for a game begins. Arguments: <c>game</c>.</summary>
    public const string CompactGame = "activity.step.compact.game";

    /// <summary>A game's space was freed. Arguments: <c>game</c>, <c>bytes</c>.</summary>
    public const string CompactFreed = "activity.step.compact.freed";

    /// <summary>There was nothing to free up for a game. Arguments: <c>game</c>.</summary>
    public const string CompactNothingFreed = "activity.step.compact.nothingFreed";

    /// <summary>A game's recently removed snapshots the player agreed to lose were deleted, which cannot be undone. Arguments: <c>game</c>.</summary>
    public const string CompactRemovedDeleted = "activity.step.compact.removedDeleted";

    /// <summary>
    /// Recently removed snapshots the player agreed to lose were kept, because one of their folders took the snapshot off a profile again
    /// after the player agreed. Arguments: <c>game</c>, <c>count</c>.
    /// </summary>
    public const string CompactRemovedKept = "activity.step.compact.removedKept";

    /// <summary>Space could not be freed in one folder of a game. Arguments: <c>game</c>, <c>folder</c>.</summary>
    public const string CompactFolderFailed = "activity.step.compact.folderFailed";

    /// <summary>Moving the snapshot folder begins. Arguments: <c>from</c>, <c>to</c>, <c>files</c>, <c>bytes</c>.</summary>
    public const string RelocateStart = "activity.step.relocate.start";

    /// <summary>
    /// One folder of a game is being copied. Arguments: <c>game</c>, the game's name; <c>part</c>, which folder it is
    /// (<c>install</c> for the game's files, <c>saves</c> for its save folders, <c>other</c> for a folder that is neither);
    /// <c>files</c>, how many files it holds. A move names no file and no folder of the engine's own.
    /// </summary>
    public const string RelocateFolder = "activity.step.relocate.folder";

    /// <summary>Every file is copied and checked. No arguments.</summary>
    public const string RelocateCopied = "activity.step.relocate.copied";

    /// <summary>SavePoint now uses the new folder. Arguments: <c>path</c>.</summary>
    public const string RelocateSwitched = "activity.step.relocate.switched";

    /// <summary>The old copies are being removed. No arguments.</summary>
    public const string RelocateRemovingOld = "activity.step.relocate.removingOld";

    /// <summary>
    /// An old copy could not be removed and stays where it was. Arguments: <c>path</c>, the game's folder in the old location
    /// (never the engine's own folder inside it); <c>part</c>, <c>install</c>, <c>saves</c> or <c>other</c> as for <see cref="RelocateFolder"/>.
    /// </summary>
    public const string RelocateNotRemoved = "activity.step.relocate.notRemoved";

    /// <summary>The move stopped, so the copy it made is being taken back. Arguments: <c>path</c>.</summary>
    public const string RelocateTakingBack = "activity.step.relocate.takingBack";

    /// <summary>Clearing SavePoint's data begins. Arguments: <c>games</c>.</summary>
    public const string ClearStart = "activity.step.clear.start";

    /// <summary>One game's snapshots are being removed. Arguments: <c>game</c>.</summary>
    public const string ClearGame = "activity.step.clear.game";

    /// <summary>The folders SavePoint made in the snapshot folder are being removed. Arguments: <c>count</c>, how many were found.</summary>
    public const string ClearFolders = "activity.step.clear.folders";

    /// <summary>The game list and settings are being removed. No arguments.</summary>
    public const string ClearSettings = "activity.step.clear.settings";

    /// <summary>The cover art is being removed. No arguments.</summary>
    public const string ClearArtwork = "activity.step.clear.artwork";

    /// <summary>The log folder is being emptied. No arguments.</summary>
    public const string ClearLogs = "activity.step.clear.logs";

    /// <summary>
    /// Something could not be removed and stays where it was. Arguments: <c>path</c>, what stayed (for a snapshot folder, the
    /// game's folder, never the engine's own folder inside it); <c>part</c>, <c>install</c>, <c>saves</c> or <c>other</c> as for <see cref="RelocateFolder"/>.
    /// </summary>
    public const string ClearNotRemoved = "activity.step.clear.notRemoved";

    /// <summary>Clearing is over. Arguments: <c>left</c>, how many items could not be removed.</summary>
    public const string ClearDone = "activity.step.clear.done";
}
