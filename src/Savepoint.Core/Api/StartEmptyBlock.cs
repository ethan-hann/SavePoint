namespace Savepoint.Core.Api;

/// <summary>Why a folder cannot start empty in a new profile; <see cref="None"/> when it can, or when the folder never starts empty.</summary>
public enum StartEmptyBlock
{
    /// <summary>Nothing stands in the way, or the folder is the install folder, which never starts empty.</summary>
    None,

    /// <summary>The game has no profile yet, so no snapshot holds the folder's files: the profile being made is the first one.</summary>
    FirstProfile,

    /// <summary>The folder has new or changed files since its profile's last snapshot.</summary>
    NewChanges,

    /// <summary>A switch of the folder stopped part way, or the folder could not be checked, so it cannot be told whether a snapshot holds its files.</summary>
    StoppedSwitch,

    /// <summary>Another reason: a file is held by another program, the folder sits on an older snapshot, or it is not on one of the game's profiles.</summary>
    Other,
}
