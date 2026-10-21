namespace Savepoint.Core.Tests.Services;

// A clock that only moves when a test advances it; timers fire on the advancing thread, in due order.
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private readonly DateTimeOffset _origin;
    private TimeSpan _elapsed;

    public ManualTimeProvider(DateTimeOffset? origin = null)
    {
        _origin = origin ?? new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
    }

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _origin + _elapsed;
        }
    }

    public override long GetTimestamp()
    {
        lock (_gate)
        {
            return _elapsed.Ticks;
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    public int PendingTimers
    {
        get
        {
            lock (_gate)
            {
                return _timers.Count(t => t.IsArmed);
            }
        }
    }

    public void Advance(TimeSpan by)
    {
        TimeSpan target;
        lock (_gate)
        {
            target = _elapsed + by;
        }

        while (true)
        {
            ManualTimer? next;
            lock (_gate)
            {
                next = _timers.Where(t => t.IsArmed && t.Due <= target).OrderBy(t => t.Due).FirstOrDefault();
                if (next is null)
                {
                    _elapsed = target;
                    return;
                }

                _elapsed = next.Due > _elapsed ? next.Due : _elapsed;
                next.FireAndRearm();
            }

            next.Callback(next.State);
        }
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        private TimeSpan? _due;
        private TimeSpan _period = Timeout.InfiniteTimeSpan;

        public TimerCallback Callback { get; } = callback;

        public object? State { get; } = state;

        public bool IsArmed => _due is not null;

        public TimeSpan Due => _due!.Value;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner._gate)
            {
                if (!owner._timers.Contains(this))
                {
                    owner._timers.Add(this);
                }

                _due = dueTime == Timeout.InfiniteTimeSpan ? null : owner._elapsed + dueTime;
                _period = period;
                return true;
            }
        }

        // Called with the provider's gate held.
        public void FireAndRearm() => _due = _period == Timeout.InfiniteTimeSpan || _period == TimeSpan.Zero ? null : _due + _period;

        public void Dispose()
        {
            lock (owner._gate)
            {
                _due = null;
                owner._timers.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
