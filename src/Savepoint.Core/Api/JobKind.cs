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

    /// <summary>Freeing up space, for one game or all of them. Its result is an <see cref="OptimizeResult"/>.</summary>
    Compact,

    /// <summary>Moving the snapshot folder. Its result is a <see cref="StorageRelocationResult"/>.</summary>
    RelocateStorage,

    /// <summary>Removing all of SavePoint's data. Not cancellable. Its result is a <see cref="ClearAllDataResult"/>.</summary>
    ClearData,
}
