namespace Savepoint.Core.Launch;

/// <summary>The two timings of exit detection; tests pass small ones.</summary>
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
    /// Inside it, a clean exit (code 0) of a program seen under the name waits only two polling intervals, an error exit the whole grace.
    /// </summary>
    public TimeSpan RelaunchWindow { get; init; } = DefaultRelaunchWindow;

    /// <summary>Throws when the timings would spin or never wait.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The polling interval is not positive, or the grace or the relaunch window is negative.</exception>
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
    }
}
