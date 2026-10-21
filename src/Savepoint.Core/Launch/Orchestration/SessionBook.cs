using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Launch.Orchestration;

// What one game's latest session holds besides the snapshot the Api shows.
internal sealed class SessionRecord(LaunchSessionInfo info)
{
    public LaunchSessionInfo Info { get; set; } = info;

    // The program or link being followed; null until the game has started.
    public GameSessionWatcher? Watcher { get; set; }

    // The commit each folder was left on for the game to start from, which a fork or a move forward must still find the folder on.
    public IReadOnlyDictionary<RepoRole, string> StartCommits { get; set; } = new Dictionary<RepoRole, string>();

    public bool IsOpen => Info.State is not (SessionState.Completed or SessionState.Failed);
}

// Every game's latest session, for the life of the process (D13.4). All changes go through here, under one lock, so the revision
// of a game's events only grows and no event is ever raised while the lock is held.
internal sealed class SessionBook : IGameSessions
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, SessionRecord> _records = new(StringComparer.Ordinal);
    private readonly HashSet<string> _starting = new(StringComparer.Ordinal);
    private readonly EventPump _pump = new();
    private long _revision;

    public event EventHandler<LaunchSessionInfo>? SessionChanged;

    public event EventHandler? Changed;

    public bool IsGameRunning(string gameId)
    {
        lock (_gate)
        {
            return _starting.Contains(gameId) || (_records.TryGetValue(gameId, out var record) && record.IsOpen);
        }
    }

    // True while the game is being launched or played, as opposed to waiting for the player's choice after the game has stopped.
    public bool IsInUse(string gameId)
    {
        lock (_gate)
        {
            return _starting.Contains(gameId)
                || (_records.TryGetValue(gameId, out var record) && record.Info.State is SessionState.Preparing or SessionState.Switching or SessionState.Running);
        }
    }

    public string? RunningProfileId(string gameId)
    {
        lock (_gate)
        {
            return _records.TryGetValue(gameId, out var record) && record.IsOpen ? record.Info.ProfileId : null;
        }
    }

    public IReadOnlyList<string> RunningGameIds()
    {
        lock (_gate)
        {
            return [.. _records.Where(pair => pair.Value.IsOpen).Select(pair => pair.Key).Union(_starting).Order(StringComparer.Ordinal)];
        }
    }

    public LaunchSessionInfo? Get(string gameId)
    {
        lock (_gate)
        {
            return _records.TryGetValue(gameId, out var record) ? record.Info : null;
        }
    }

    public IReadOnlyList<LaunchSessionInfo> List()
    {
        lock (_gate)
        {
            return [.. _records.Values.Select(record => record.Info).OrderBy(info => info.GameId, StringComparer.Ordinal)];
        }
    }

    public SessionRecord? Record(string gameId)
    {
        lock (_gate)
        {
            return _records.GetValueOrDefault(gameId);
        }
    }

    // Claims the game for a launch that is still being checked, so two calls made at the same moment cannot both go on. False
    // when the game already has an open session or another launch is being checked.
    public bool TryClaim(string gameId)
    {
        lock (_gate)
        {
            if (_starting.Contains(gameId) || (_records.TryGetValue(gameId, out var record) && record.IsOpen))
            {
                return false;
            }

            _starting.Add(gameId);
        }

        RaiseChanged();
        return true;
    }

    public void Unclaim(string gameId)
    {
        lock (_gate)
        {
            _starting.Remove(gameId);
        }

        RaiseChanged();
    }

    // Opens the game's new session, replacing the finished one, and ends the claim. The old session's handle on a program that may
    // still be running is released here; the program itself is left alone.
    public LaunchSessionInfo Open(string gameId, string profileId, LaunchTarget target, bool startedFromOlderPoint, Guid launchJobId)
    {
        GameSessionWatcher? replaced;
        LaunchSessionInfo info;
        lock (_gate)
        {
            replaced = _records.GetValueOrDefault(gameId)?.Watcher;
            info = new LaunchSessionInfo(gameId, profileId, target, startedFromOlderPoint, launchJobId, new SessionState.Preparing(), ++_revision);
            _records[gameId] = new SessionRecord(info);
            _starting.Remove(gameId);
            Announce(info);
        }

        replaced?.Dispose();
        return info;
    }

    // Moves the session that the launch job started to a new state. The change function sees the record under the lock and
    // answers the new state, or null to leave things as they are (the session moved on, or was replaced, while the caller worked).
    public bool TryChange(string gameId, Guid launchJobId, Func<SessionRecord, SessionState?> change)
    {
        lock (_gate)
        {
            if (!_records.TryGetValue(gameId, out var record) || record.Info.LaunchJobId != launchJobId || change(record) is not { } state)
            {
                return false;
            }

            record.Info = record.Info with { State = state, Revision = ++_revision };
            Announce(record.Info);
            return true;
        }
    }

    // Runs work on the session's record under the lock without changing its state, for the facts only the orchestrator reads.
    public bool TryUpdate(string gameId, Guid launchJobId, Action<SessionRecord> update)
    {
        lock (_gate)
        {
            if (!_records.TryGetValue(gameId, out var record) || record.Info.LaunchJobId != launchJobId)
            {
                return false;
            }

            update(record);
            return true;
        }
    }

    // Why a session cannot be completed now, or null when it can. A session that is over, or was never started, has nothing to
    // complete; one that is still being prepared or played is not finished yet.
    public static Exception? CannotComplete(string gameId, SessionRecord? record) => record?.Info.State switch
    {
        null or SessionState.Completing or SessionState.Completed or SessionState.Failed => new NoActiveSessionException(gameId),
        SessionState.Exited => null,
        _ => new SessionStillRunningException(gameId),
    };

    // Moves an exited session on while holding the lock, so two completions made at the same moment cannot both go ahead. The
    // start function runs under the lock and answers the new state; it may start the capture job, which only schedules work.
    public void Complete(string gameId, Func<SessionRecord, SessionState> start)
    {
        lock (_gate)
        {
            var record = _records.GetValueOrDefault(gameId);
            if (CannotComplete(gameId, record) is { } refusal)
            {
                throw refusal;
            }

            record!.Info = record.Info with { State = start(record), Revision = ++_revision };
            Announce(record.Info);
        }
    }

    // Moves whichever session has this job as its launch job or its completing job; the lock stays with the caller's function.
    public bool TryChangeByJob(Guid jobId, Func<SessionRecord, SessionState?> change)
    {
        lock (_gate)
        {
            var record = _records.Values.FirstOrDefault(candidate =>
                candidate.Info.LaunchJobId == jobId || (candidate.Info.State is SessionState.Completing completing && completing.JobId == jobId));
            if (record is null || change(record) is not { } state)
            {
                return false;
            }

            record.Info = record.Info with { State = state, Revision = ++_revision };
            Announce(record.Info);
            return true;
        }
    }

    // The caller holds the lock; the events are queued under it so they leave in the order the changes were made.
    private void Announce(LaunchSessionInfo info)
    {
        _pump.Post(() => SessionChanged?.Invoke(this, info));
        _pump.Post(() => Changed?.Invoke(this, EventArgs.Empty));
    }

    private void RaiseChanged() => _pump.Post(() => Changed?.Invoke(this, EventArgs.Empty));
}
