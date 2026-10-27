namespace Savepoint.Core.Launch;

/// <summary>How long a launch waits for the Steam client it started to come up signed in; tests pass small timings.</summary>
public sealed record SteamWaitOptions
{
    /// <summary>The longest wait: ninety seconds. A client that needs a password or a Steam Guard code never signs in on its own, so the wait has an end.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(90);

    /// <summary>How often Steam is looked at while waiting: one second.</summary>
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(1);

    /// <summary>How long to wait before giving up; must not be negative.</summary>
    public TimeSpan Timeout { get; init; } = DefaultTimeout;

    /// <summary>Time between two looks at Steam; must be positive.</summary>
    public TimeSpan PollInterval { get; init; } = DefaultPollInterval;
}
