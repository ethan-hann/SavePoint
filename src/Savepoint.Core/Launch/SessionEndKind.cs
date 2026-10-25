namespace Savepoint.Core.Launch;

/// <summary>How a play session was found to be over.</summary>
public enum SessionEndKind
{
    /// <summary>
    /// The program SavePoint started exited, and no process name was configured to follow; or the program was itself the named
    /// process and its exit was the game quitting (a long run, or a quick exit with no new copy within the grace). The end time is then
    /// the program's exit.
    /// </summary>
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

    /// <summary>
    /// The program SavePoint started ran for longer than the relaunch window and exited, and the named process never showed up within the
    /// grace after that. Nothing says the program was not the game, so its run is the play time and the end time is its exit; the name
    /// is probably wrong. A launcher that handed over late still has the grace to be followed before this is decided.
    /// </summary>
    NamedProcessNeverMatched,

    /// <summary>
    /// The process list could not be read at any look during the whole grace, so the watcher cannot say whether the named process ran.
    /// The name is not to blame, and the end time includes the wait.
    /// </summary>
    ProcessListUnreadable,

    /// <summary>The player said they were done; the game may still be running.</summary>
    ManuallyFinished,
}
