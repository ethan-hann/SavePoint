using Savepoint.Core.Api;

namespace Savepoint.App.Tests.Contract;

// What a subscriber to JobChanged and SessionChanged sees over a story. The real backend raises them from its own threads after the
// call has returned and sends only the newest state of a job that changed twice in a row, so the fake delivers in the background and
// the story compares the part both can meet: the states a job or session passes through, in order, with revisions that only rise.
internal sealed class ContractEvents : IDisposable
{
    private static readonly TimeSpan _limit = TimeSpan.FromSeconds(60);

    private readonly object _gate = new();
    private readonly List<JobSnapshot> _jobs = [];
    private readonly List<LaunchSessionInfo> _sessions = [];
    private readonly ContractHarness _backend;

    public ContractEvents(ContractHarness backend)
    {
        _backend = backend;
        backend.Jobs.JobChanged += OnJobChanged;
        backend.Launch.SessionChanged += OnSessionChanged;
    }

    public void Dispose()
    {
        _backend.Jobs.JobChanged -= OnJobChanged;
        _backend.Launch.SessionChanged -= OnSessionChanged;
    }

    // Returns once the newest state the backend reports for each job and session has reached the subscriber.
    public async Task SettledAsync(CancellationToken cancellationToken)
    {
        var until = DateTime.UtcNow + _limit;
        while (!Delivered())
        {
            if (DateTime.UtcNow > until)
            {
                throw new TimeoutException("The newest job or session state never reached the subscriber.");
            }

            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        }
    }

    // What only the real backend does on its own, which the compared streams leave out: it queues a compaction after a save, whether
    // that one ends or is stopped by the next launch depends on timing, and it reports a session as switching while it copies files.
    public IReadOnlyList<string> OnlyTheBackendDoesOnItsOwn()
    {
        lock (_gate)
        {
            var kinds = _jobs.Where(job => IsBackgroundWork(job.Kind)).Select(job => job.Kind.ToString()).Distinct().Order(StringComparer.Ordinal);
            return
            [
                $"jobs the backend started by itself: {(_jobs.Any(job => IsBackgroundWork(job.Kind)) ? string.Join(", ", kinds) : "none")}",
                $"a session reported as switching: {_sessions.Any(session => session.State is SessionState.Switching)}",
            ];
        }
    }

    public IReadOnlyList<string> Describe()
    {
        lock (_gate)
        {
            var lines = new List<string>();
            foreach (var job in _jobs.Where(snapshot => !IsBackgroundWork(snapshot.Kind)).GroupBy(snapshot => snapshot.JobId))
            {
                var events = job.ToList();
                var last = events[^1];
                var now = _backend.Jobs.Get(last.JobId);
                lines.Add(
                    $"{last.Kind} of {last.GameId ?? "every game"}: {string.Join(" > ", Collapse(events.Select(e => e.Status.ToString())))}; "
                    + $"revisions rise={Rises(events.Select(e => e.Revision))}; last event is the job as it now stands={now is null || now.Status == last.Status && now.Revision == last.Revision}; "
                    + $"result={last.Result?.GetType().Name ?? "none"}; error={last.Error?.MessageId ?? "none"}");
            }

            foreach (var game in _sessions.GroupBy(session => session.GameId))
            {
                var events = game.Where(session => session.State is not SessionState.Switching).ToList();
                var last = events[^1];
                var now = _backend.Launch.GetSession(game.Key);
                lines.Add(
                    $"session of {game.Key}: {string.Join(" > ", Collapse(events.Select(e => e.State.GetType().Name)))}; "
                    + $"revisions rise={Rises(events.Select(e => e.Revision))}; last event is the session as it now stands={now is null || now.Revision == last.Revision}");
            }

            return lines;
        }
    }

    private static bool IsBackgroundWork(JobKind kind) => kind is JobKind.Compact or JobKind.PackStorage;

    private bool Delivered()
    {
        lock (_gate)
        {
            return _backend.Jobs.List().All(job => _jobs.Any(e => e.JobId == job.JobId && e.Revision >= job.Revision))
                && _backend.Launch.ListSessions().All(session => _sessions.Any(e => e.GameId == session.GameId && e.Revision >= session.Revision));
        }
    }

    private static IEnumerable<string> Collapse(IEnumerable<string> states)
    {
        string? previous = null;
        foreach (var state in states)
        {
            if (state != previous)
            {
                yield return state;
            }

            previous = state;
        }
    }

    private static bool Rises(IEnumerable<long> revisions)
    {
        var list = revisions.ToList();
        return list.Zip(list.Skip(1), (before, after) => after > before).All(rises => rises);
    }

    private void OnJobChanged(object? sender, JobSnapshot snapshot)
    {
        lock (_gate)
        {
            _jobs.Add(snapshot);
        }
    }

    private void OnSessionChanged(object? sender, LaunchSessionInfo session)
    {
        lock (_gate)
        {
            _sessions.Add(session);
        }
    }
}
