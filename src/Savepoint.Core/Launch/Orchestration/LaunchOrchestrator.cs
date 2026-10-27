using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Platform;

namespace Savepoint.Core.Launch.Orchestration;

/// <summary>
/// The real <see cref="ILaunchApi"/>: previews what a launch would change, switches the game's folders, starts the game, follows
/// the play session and carries out the player's post-play choice. It is built once for the life of the process, from the same
/// services as every other Api, and holds every game's latest session (D13.4): a storage move or a settings change never drops one.
/// </summary>
public sealed partial class LaunchOrchestrator : ILaunchApi
{
    private readonly BackendServices _services;
    private readonly GameStarter _starter;
    private readonly SessionBook _book;
    private readonly ISteamClient _steam;
    private readonly SteamWaitOptions _steamWait;

    private LaunchOrchestrator(BackendServices services, LaunchOrchestratorOptions options)
    {
        _services = services;
        _book = new SessionBook(services.Log);
        _steam = options.Steam ?? services.Platform.Steam;
        _steamWait = options.SteamWait ?? new SteamWaitOptions();
        _steamWait.EnsureValid();
        _starter = options.Starter ?? GameStarter.For(services.Platform, options.Session, services.Time);

        // The orchestrator lives as long as the process, so it follows the registry's jobs for as long.
        services.Jobs.JobChanged += OnJobChanged;
    }

    /// <inheritdoc />
    public event EventHandler<LaunchSessionInfo>? SessionChanged
    {
        add => _book.SessionChanged += value;
        remove => _book.SessionChanged -= value;
    }

    /// <summary>
    /// A factory for <see cref="BackendOptions.Launch"/>: the backend calls it once, with its shared services, and attaches the
    /// sessions it returns.
    /// </summary>
    /// <param name="options">What to build the orchestrator from; the defaults when null.</param>
    public static Func<BackendServices, LaunchComponents> Factory(LaunchOrchestratorOptions? options = null) =>
        services => Create(services, options);

    /// <summary>Builds the orchestrator over the backend's shared services.</summary>
    /// <param name="services">The catalog, jobs, game lock and storage the orchestrator works with.</param>
    /// <param name="options">What to build it from; the defaults when null.</param>
    /// <returns>The Api and the sessions the services consult to refuse destructive work on a running game (D13.5).</returns>
    public static LaunchComponents Create(BackendServices services, LaunchOrchestratorOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var orchestrator = new LaunchOrchestrator(services, options ?? new LaunchOrchestratorOptions());
        return new LaunchComponents(orchestrator, orchestrator._book);
    }

    /// <inheritdoc />
    public LaunchSessionInfo? GetSession(string gameId) => _book.Get(gameId);

    /// <inheritdoc />
    public IReadOnlyList<LaunchSessionInfo> ListSessions() => _book.List();

}
