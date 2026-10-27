namespace Savepoint.Core.Api;

/// <summary>What a job does.</summary>
public enum JobKind
{
    /// <summary>Preparing, switching folders, and starting the game. The play session itself continues in <see cref="ILaunchApi"/>.</summary>
    Launch,

    /// <summary>Preparing and switching folders without starting the game. No play session follows.</summary>
    Switch,

    /// <summary>Taking a snapshot of the folders. Its result is a <see cref="CaptureResult"/>.</summary>
    Capture,

    /// <summary>Freeing up space, for one game or all of them, which the player asks for or which follows deleting a profile. Its result is an <see cref="OptimizeResult"/>.</summary>
    Compact,

    /// <summary>Moving the snapshot folder. Its result is a <see cref="StorageRelocationResult"/>.</summary>
    RelocateStorage,

    /// <summary>Removing all of SavePoint's data. Not cancellable. Its result is a <see cref="ClearAllDataResult"/>.</summary>
    ClearData,

    /// <summary>
    /// Packing a game's snapshot files tighter on disk, which SavePoint starts by itself after a snapshot. It starts no new deletion of
    /// anything a player can see, so it is not <see cref="Compact"/>: the player's Free up space is the one that deletes Recently removed.
    /// It does finish a Free up space the player already confirmed that was cut off before it was done, as every run does. Always for one
    /// game and always cancellable. Its result is an <see cref="OptimizeResult"/>.
    /// </summary>
    PackStorage,
}
