namespace Savepoint.App.Features.Launch;

/// <summary>
/// The time guesses the launch flow shows before and during a switch. The service's own estimate is preferred when it
/// has one; these cover the rest from one fixed throughput.
/// </summary>
public static class LaunchTiming
{
    /// <summary>
    /// How many bytes a switch is assumed to write per second (50 MB/s, a typical sustained rate for a large mixed
    /// set of game files on a mid-range drive). It is only a stand-in until measured progress is available.
    /// </summary>
    public const long AssumedBytesPerSecond = 50_000_000;

    /// <summary>How long a switch must have run before its measured rate is trusted over the assumed one.</summary>
    public static readonly TimeSpan MeasureAfter = TimeSpan.FromSeconds(5);

    /// <summary>How long replacing <paramref name="bytes"/> would take at <see cref="AssumedBytesPerSecond"/>.</summary>
    public static TimeSpan Estimate(long bytes) =>
        TimeSpan.FromSeconds(Math.Max(0, (double)bytes / AssumedBytesPerSecond));

    /// <summary>
    /// How long a running switch has left: the measured rate once <see cref="MeasureAfter"/> has passed, the assumed
    /// rate before that. Null when there is nothing left to do or no total to compare with.
    /// </summary>
    public static TimeSpan? Remaining(long bytesDone, long bytesTotal, TimeSpan elapsed)
    {
        if (bytesTotal <= 0 || bytesDone >= bytesTotal)
        {
            return null;
        }

        var rate = bytesDone > 0 && elapsed >= MeasureAfter ? bytesDone / elapsed.TotalSeconds : AssumedBytesPerSecond;
        return TimeSpan.FromSeconds((bytesTotal - bytesDone) / rate);
    }

    /// <summary>
    /// How long a running switch has left from the share of it that is done, for work that is counted in files and not priced in bytes
    /// (git removes files before it writes any). Null before <see cref="MeasureAfter"/> has passed, and when the share is nothing,
    /// everything or outside that range.
    /// </summary>
    /// <param name="share">How much is done, from 0 to 1.</param>
    /// <param name="elapsed">How long the switch has run.</param>
    public static TimeSpan? RemainingAtShare(double share, TimeSpan elapsed) =>
        share is > 0 and < 1 && elapsed >= MeasureAfter ? elapsed * ((1 - share) / share) : null;

    /// <summary>Rounds a time for display: to the nearest 10 seconds under a minute (never below 10), otherwise to the nearest minute.</summary>
    public static TimeSpan RoundForDisplay(TimeSpan span)
    {
        if (span < TimeSpan.FromMinutes(1))
        {
            return TimeSpan.FromSeconds(Math.Max(10, Math.Round(span.TotalSeconds / 10, MidpointRounding.AwayFromZero) * 10));
        }

        return TimeSpan.FromMinutes(Math.Round(span.TotalMinutes, MidpointRounding.AwayFromZero));
    }
}
