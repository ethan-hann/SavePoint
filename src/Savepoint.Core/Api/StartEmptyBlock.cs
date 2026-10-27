namespace Savepoint.Core.Api;

/// <summary>Why a folder cannot start empty in a new profile; <see cref="None"/> when it can, or when the folder never starts empty.</summary>
public enum StartEmptyBlock
{
    /// <summary>Nothing stands in the way, or the folder is the install folder, which never starts empty.</summary>
    None,

    /// <summary>The game has no profile yet, so no snapshot holds the folder's files: the profile being made is the first one.</summary>
    FirstProfile,

    /// <summary>The folder has new or changed files since its profile's last snapshot, or SavePoint has no marker in it, so its files are in no snapshot. Files the game leaves out do not count.</summary>
    NewChanges,

    /// <summary>SavePoint has no marker in the folder (the player made it again by hand, or it was never confirmed), so no snapshot can be taken of it until the player confirms it in Edit game.</summary>
    NotConfirmed,

    /// <summary>A switch of the folder stopped part way, so it cannot be told whether a snapshot holds its files until the switch is finished or discarded.</summary>
    StoppedSwitch,

    /// <summary>Another reason: a file is held by another program, reading the folder failed, the folder sits on an older snapshot, or it is not on one of the game's profiles.</summary>
    Other,
}
