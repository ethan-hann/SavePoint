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
}
