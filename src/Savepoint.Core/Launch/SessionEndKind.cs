namespace Savepoint.Core.Launch;

/// <summary>How a play session was found to be over.</summary>
public enum SessionEndKind
{
    /// <summary>The program SavePoint started exited, and no process name was configured to follow.</summary>
    ChildExited,

    /// <summary>
    /// The named process was seen running and is gone, after the program SavePoint started (or the link's handler) was done too.
    /// This covers a launcher stub that handed over to the real game: the stub's own exit only starts the wait for the game.
    /// </summary>
    NamedProcessEnded,

    /// <summary>
    /// The named process never showed up within the grace period (counted from the stub's exit, or from the link being opened).
    /// The game may still be running under a different name, so the session is over only as far as SavePoint can tell.
    /// </summary>
    NamedProcessNeverAppeared,

    /// <summary>The player said they were done; the game may still be running.</summary>
    ManuallyFinished,
}
