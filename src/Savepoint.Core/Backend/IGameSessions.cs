namespace Savepoint.Core.Backend;

/// <summary>
/// What the services need to know about play sessions, which the launch side owns: whether a game is running, which profile
/// it runs, and when that changes. The services use it to refuse destructive work on a running game (D13.5) and to mark the
/// running profile in the profile list.
/// </summary>
/// <remarks>
/// A game counts as running from the moment a launch is prepared until its session is completed or failed, so the switch
/// and the post-play prompt are covered as well as the play itself. Implementations must be thread-safe.
/// </remarks>
public interface IGameSessions
{
    /// <summary>Raised on any thread after the set of running games or profiles changed.</summary>
    event EventHandler? Changed;

    /// <summary>True while a session is open for the game.</summary>
    /// <param name="gameId">The game to ask about.</param>
    bool IsGameRunning(string gameId);

    /// <summary>The profile of the game's open session, or null when none is open.</summary>
    /// <param name="gameId">The game to ask about.</param>
    string? RunningProfileId(string gameId);

    /// <summary>The ids of every game with an open session.</summary>
    IReadOnlyList<string> RunningGameIds();
}

/// <summary>Reports nothing running; what the services use until a launch component supplies the real sessions.</summary>
internal sealed class NullGameSessions : IGameSessions
{
    public event EventHandler? Changed
    {
        add { }
        remove { }
    }

    public bool IsGameRunning(string gameId) => false;

    public string? RunningProfileId(string gameId) => null;

    public IReadOnlyList<string> RunningGameIds() => [];
}

/// <summary>
/// Stands in for the sessions the services were built with while the launch component, which needs the services to be built
/// first, is created. Answers from whatever is attached last.
/// </summary>
internal sealed class ForwardingGameSessions : IGameSessions
{
    private readonly Lock _gate = new();
    private IGameSessions _target = new NullGameSessions();

    public event EventHandler? Changed;

    public void Attach(IGameSessions sessions)
    {
        IGameSessions previous;
        lock (_gate)
        {
            previous = _target;
            _target = sessions;
        }

        previous.Changed -= OnChanged;
        sessions.Changed += OnChanged;
        OnChanged(this, EventArgs.Empty);
    }

    public bool IsGameRunning(string gameId) => Current().IsGameRunning(gameId);

    public string? RunningProfileId(string gameId) => Current().RunningProfileId(gameId);

    public IReadOnlyList<string> RunningGameIds() => Current().RunningGameIds();

    private IGameSessions Current()
    {
        lock (_gate)
        {
            return _target;
        }
    }

    private void OnChanged(object? sender, EventArgs e) => Changed?.Invoke(this, EventArgs.Empty);
}
