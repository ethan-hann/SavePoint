using Savepoint.Core.Api;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Fakes;

// Nothing runs on its own: a test scripts every job and session step, and events fire on the thread that made the change.
public sealed class FakeBackend
{
    private readonly object _gate = new();
    private readonly List<Action> _pending = [];
    private readonly Dictionary<string, Queue<ErrorInfo>> _failures = [];

    public FakeBackend(ScenarioBuilder scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        Clock = new FixedTimeProvider(scenario.Now);
        State = new FakeState(scenario);
        Jobs = new FakeJobsApi(this);
        Launch = new FakeLaunchApi(this);
        Games = new FakeGamesApi(this);
        Profiles = new FakeProfilesApi(this);
        Settings = new FakeSettingsApi(this);
        History = new FakeHistoryApi(this);
        AppStatus = new FakeAppStatusApi(this);
        Artwork = new FakeArtworkApi(this);
    }

    public FixedTimeProvider Clock { get; }

    public FakeCallLog Calls { get; } = new();

    public FakeGamesApi Games { get; }

    public FakeProfilesApi Profiles { get; }

    public FakeLaunchApi Launch { get; }

    public FakeSettingsApi Settings { get; }

    public FakeHistoryApi History { get; }

    public FakeJobsApi Jobs { get; }

    public FakeAppStatusApi AppStatus { get; }

    public FakeArtworkApi Artwork { get; }

    internal FakeState State { get; }

    public static FakeBackend Sample() => new(ScenarioBuilder.Sample());

    public static FakeBackend Empty() => new(ScenarioBuilder.Empty());

    // Queues one failure for the next call to TApi's member; queue several to fail several calls in a row.
    public void FailNext<TApi>(string member, ErrorInfo error)
    {
        ArgumentNullException.ThrowIfNull(error);
        lock (_gate)
        {
            var key = Key(typeof(TApi).Name, member);
            if (!_failures.TryGetValue(key, out var queue))
            {
                queue = new Queue<ErrorInfo>();
                _failures[key] = queue;
            }

            queue.Enqueue(error);
        }
    }

    // Records the call, applies a queued failure, otherwise runs the body; a thrown exception maps like the facade's.
    internal Task<OperationResult<T>> Call<T>(string api, string member, object?[] args, Func<OperationResult<T>> body)
    {
        Calls.Record(api, member, args);
        OperationResult<T> result;
        lock (_gate)
        {
            if (TakeFailure(api, member) is { } failure)
            {
                return Task.FromResult(OperationResult.Failure<T>(failure));
            }

            try
            {
                result = body();
            }
            catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or SavepointException)
            {
                result = OperationResult.Failure<T>(ex);
            }
        }

        RaisePending();
        return Task.FromResult(result);
    }

    // Snapshot reads are recorded too, but never fail.
    internal T Read<T>(string api, string member, object?[] args, Func<T> body)
    {
        Calls.Record(api, member, args);
        lock (_gate)
        {
            return body();
        }
    }

    // A test's own look at the state, kept out of the call log.
    internal T Peek<T>(Func<T> body)
    {
        lock (_gate)
        {
            return body();
        }
    }

    // A test's scripting step; misuse throws so a broken script fails loudly.
    internal void Script(Action body)
    {
        lock (_gate)
        {
            body();
        }

        RaisePending();
    }

    // Must be called under the lock; the event fires once the outermost call releases it.
    internal void Raise(Action raise) => _pending.Add(raise);

    private static string Key(string api, string member) => $"{api}.{member}";

    private ErrorInfo? TakeFailure(string api, string member) =>
        _failures.TryGetValue(Key(api, member), out var queue) && queue.TryDequeue(out var error) ? error : null;

    private void RaisePending()
    {
        List<Action> pending;
        lock (_gate)
        {
            pending = [.. _pending];
            _pending.Clear();
        }

        foreach (var raise in pending)
        {
            raise();
        }
    }
}
