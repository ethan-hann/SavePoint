namespace Savepoint.Core.Launch;

/// <summary>The two timings of exit detection; tests pass small ones.</summary>
public sealed record GameSessionOptions
{
    /// <summary>How often the process list is read while a named process is followed: two seconds.</summary>
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(2);

    /// <summary>How long the named process has to appear before the session settles without it: thirty seconds (D13.10).</summary>
    public static readonly TimeSpan DefaultGrace = TimeSpan.FromSeconds(30);

    /// <summary>Time between reads of the process list; must be positive.</summary>
    public TimeSpan PollInterval { get; init; } = DefaultPollInterval;

    /// <summary>
    /// How long the named process may stay unseen. It counts from the moment the program SavePoint started exits (a launcher stub
    /// that has handed over), or from the link being opened when there is no program; it never runs while that program is alive.
    /// </summary>
    public TimeSpan Grace { get; init; } = DefaultGrace;

    /// <summary>Throws when the timings would spin or never wait.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The polling interval is not positive or the grace is negative.</exception>
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
    }
}
