using Savepoint.Core.Api;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Fakes;

// How the fake's JobChanged and SessionChanged events reach a subscriber. The real backend raises them from its own threads after the
// call that caused them has returned, so a screen is only shown to cope with that under Queued or Background.
public enum EventDelivery
{
    // On the thread that made the change, before the call returns.
    Inline,

    // Held back until the test calls DeliverQueuedEvents, then raised on the thread that calls it.
    Queued,

    // Raised in order by a pool thread after the call has returned; WhenEventsDeliveredAsync waits for the last one.
    Background,
}

// Nothing runs on its own: a test scripts every job and session step, and events fire on the thread that made the change (see EventDelivery).
public sealed class FakeBackend
{
    private readonly object _gate = new();
    private readonly List<Action> _pending = [];
    private readonly List<Action> _queued = [];
    private readonly List<Exception> _deliveryFailures = [];
    private Task? _drain;
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

    // Set before the first call; changing it while events wait delivers them under the new mode.
    public EventDelivery Delivery { get; set; }

    // The storage folder the settings name cannot be reached any more, as after an unplugged drive: the startup check says so, and
    // only then do the two startup choices for a lost folder apply.
    public FakeBackend WithStorageGone()
    {
        Script(() =>
        {
            var root = State.Settings.StorageRoot;
            State.Startup = State.Startup with { Storage = new StorageStatus(false, root, ErrorInfo.From(new StorageUnreachableException(root, null))) };
        });
        return this;
    }

    public FakeBackend WithDelivery(EventDelivery delivery)
    {
        Delivery = delivery;
        return this;
    }

    // Raises every event Queued delivery is holding, in order, on the calling thread.
    public void DeliverQueuedEvents()
    {
        foreach (var raise in TakeQueued())
        {
            raise();
        }
    }

    // Returns once every event raised so far has reached its subscribers; rethrows what a subscriber threw on the delivery thread.
    public async Task WhenEventsDeliveredAsync()
    {
        while (true)
        {
            Task? drain;
            lock (_gate)
            {
                drain = _drain;
            }

            if (drain is null)
            {
                break;
            }

            await drain.ConfigureAwait(false);
        }

        List<Exception> thrown;
        lock (_gate)
        {
            thrown = [.. _deliveryFailures];
            _deliveryFailures.Clear();
        }

        if (thrown.Count > 0)
        {
            throw new AggregateException(thrown);
        }
    }

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

    internal Task<OperationResult<T>> Call<T>(string api, string member, object?[] args, Func<OperationResult<T>> body) =>
        Call(api, member, args, CancellationToken.None, body);

    // Records the call, answers a token that is already cancelled with the cancelled error before anything runs (D23), applies a queued
    // failure, otherwise runs the body. Whatever the body throws becomes the error of its result, as the real facade's guard does.
    internal Task<OperationResult<T>> Call<T>(string api, string member, object?[] args, CancellationToken cancellationToken, Func<OperationResult<T>> body)
    {
        Calls.Record(api, member, args);
        OperationResult<T> result;
        lock (_gate)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromResult(OperationResult.Failure<T>(new RequestCancelledException()));
            }

            if (TakeFailure(api, member) is { } failure)
            {
                return Task.FromResult(OperationResult.Failure<T>(failure));
            }

            try
            {
                result = body();
            }
            catch (Exception ex)
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
            if (Delivery != EventDelivery.Inline)
            {
                _queued.AddRange(pending);
                pending = [];
                if (Delivery == EventDelivery.Background && _drain is null)
                {
                    // A thread of its own, so the events never run on the pool thread a test's await may resume on.
                    _drain = Task.Factory.StartNew(Drain, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                }
            }
        }

        foreach (var raise in pending)
        {
            raise();
        }
    }

    private List<Action> TakeQueued()
    {
        lock (_gate)
        {
            var queued = _queued.ToList();
            _queued.Clear();
            return queued;
        }
    }

    // One delivery thread at a time, so subscribers see the events in the order they were raised.
    private void Drain()
    {
        while (true)
        {
            var batch = TakeQueuedOrFinish();
            if (batch is null)
            {
                return;
            }

            foreach (var raise in batch)
            {
                try
                {
                    raise();
                }
                catch (Exception ex)
                {
                    lock (_gate)
                    {
                        _deliveryFailures.Add(ex);
                    }
                }
            }
        }
    }

    private List<Action>? TakeQueuedOrFinish()
    {
        lock (_gate)
        {
            if (_queued.Count == 0)
            {
                _drain = null;
                return null;
            }

            var queued = _queued.ToList();
            _queued.Clear();
            return queued;
        }
    }
}
