namespace Savepoint.Core.Launch;

/// <summary>The timings of exit detection; tests pass small ones.</summary>
public sealed record GameSessionOptions
{
    /// <summary>How often the process list is read while a named process is followed: two seconds.</summary>
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How long the named process has to appear before the session settles without it: two minutes. A launcher or an anti-cheat
    /// bootstrap can take longer than half a minute to start the game, and ending the session early would cut the play time and
    /// ask to save while the game is still starting. The player can end the wait at any time, so a wrong name costs little.
    /// </summary>
    public static readonly TimeSpan DefaultGrace = TimeSpan.FromMinutes(2);

    /// <summary>
    /// How long a started program may have run and still be taken for a game that quit to be started again by its store: thirty
    /// seconds. Such a game exits within seconds of starting, so a same-named process that is gone after a longer run means the
    /// player quit.
    /// </summary>
    public static readonly TimeSpan DefaultRelaunchWindow = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long the started program must have run for a clean exit (code 0) to count as the player quitting: ten seconds. A store's
    /// relaunch stub checks for the store as it starts and exits within a second or two with code 0, then the store starts the game
    /// again, while a player needs longer than that to get past the splash screens and quit.
    /// </summary>
    public static readonly TimeSpan DefaultCleanQuitMinimumRun = TimeSpan.FromSeconds(10);

    /// <summary>Time between reads of the process list; must be positive.</summary>
    public TimeSpan PollInterval { get; init; } = DefaultPollInterval;

    /// <summary>
    /// How long the named process may stay unseen. It counts from the moment the program SavePoint started exits (a launcher stub
    /// that has handed over), or from the link being opened when there is no program; it never runs while that program is alive. For a
    /// link the store's own launch helper can hold it too (see <see cref="GameSessionWatcher"/>).
    /// </summary>
    public TimeSpan Grace { get; init; } = DefaultGrace;

    /// <summary>
    /// The longest run after which the started program's exit still gets the <see cref="Grace"/> to be followed by a new copy under
    /// its own name. It applies only when the started program is itself the named process; after a longer run its exit ends the session.
    /// Inside it, a clean exit (code 0) of a program seen under the name that ran at least <see cref="CleanQuitMinimumRun"/> waits only
    /// two polling intervals; an earlier clean exit and any error exit get the whole grace.
    /// </summary>
    public TimeSpan RelaunchWindow { get; init; } = DefaultRelaunchWindow;

    /// <summary>
    /// The shortest run after which a clean exit (code 0) of the started program is taken as the player quitting, so the session ends
    /// after two polling intervals instead of the <see cref="Grace"/>. A shorter run may be a relaunch stub, which keeps the grace so
    /// the store's new copy is followed. Must not be negative.
    /// </summary>
    public TimeSpan CleanQuitMinimumRun { get; init; } = DefaultCleanQuitMinimumRun;

    /// <summary>Throws when the timings would spin or never wait.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The polling interval is not positive, or the grace, the relaunch window, or the clean quit minimum run is negative.</exception>
    internal void EnsureValid()
    {
        if (PollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(PollInterval), PollInterval, "The polling interval must be positive.");
        }

        if (Grace < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(Grace), Grace, "The grace period cannot be negative.");
        }

        if (RelaunchWindow < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(RelaunchWindow), RelaunchWindow, "The relaunch window cannot be negative.");
        }

        if (CleanQuitMinimumRun < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(CleanQuitMinimumRun), CleanQuitMinimumRun, "The clean quit minimum run cannot be negative.");
        }
    }
}
