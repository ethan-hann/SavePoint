using Savepoint.Core.Platform;

namespace Savepoint.Core.Launch.Orchestration;

/// <summary>What <see cref="LaunchOrchestrator"/> is built from. Every member is optional: the defaults start real games on this machine.</summary>
public sealed record LaunchOrchestratorOptions
{
    /// <summary>
    /// Starts games and follows their sessions. When null, the real starter for the platform is used, timed by
    /// <see cref="Session"/> and the backend's clock.
    /// </summary>
    public GameStarter? Starter { get; init; }

    /// <summary>The polling interval and grace of exit detection; the defaults when null. Ignored when <see cref="Starter"/> is given.</summary>
    public GameSessionOptions? Session { get; init; }

    /// <summary>The Steam client the launch asks before it starts a Steam game from its program; the platform's own when null.</summary>
    public ISteamClient? Steam { get; init; }

    /// <summary>How long a launch waits for a Steam client it started; the defaults when null.</summary>
    public SteamWaitOptions? SteamWait { get; init; }
}
