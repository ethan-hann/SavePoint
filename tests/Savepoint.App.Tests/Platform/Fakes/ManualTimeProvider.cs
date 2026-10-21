namespace Savepoint.App.Tests.Platform.Fakes;

// Time moves only when a test advances it, and timers fire in due order while it does.
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private long _nowTicks;

    public int ActiveTimers
    {
        get
        {
            lock (_gate)
            {
                return _timers.Count(timer => timer.IsArmed);
            }
        }
    }

    public override DateTimeOffset GetUtcNow() => new(_nowTicks + DateTime.UnixEpoch.Ticks, TimeSpan.Zero);

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        lock (_gate)
        {
            _timers.Add(timer);
        }

        timer.Change(dueTime, period);
        return timer;
    }

    public void Advance(TimeSpan by)
    {
        var target = _nowTicks + by.Ticks;
        while (true)
        {
            ManualTimer? next;
            lock (_gate)
            {
                next = _timers.Where(timer => timer.IsArmed && timer.DueTicks <= target).OrderBy(timer => timer.DueTicks).FirstOrDefault();
                if (next is null)
                {
                    _nowTicks = target;
                    return;
                }

                _nowTicks = Math.Max(_nowTicks, next.DueTicks);
            }

            next.Fire();
        }
    }

    private sealed class ManualTimer : ITimer
    {
        private readonly ManualTimeProvider _owner;
        private readonly TimerCallback _callback;
        private readonly object? _state;
        private long _periodTicks;

        public ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state)
        {
            _owner = owner;
            _callback = callback;
            _state = state;
        }

        public bool IsArmed { get; private set; }

        public long DueTicks { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (_owner._gate)
            {
                IsArmed = dueTime != Timeout.InfiniteTimeSpan;
                DueTicks = _owner._nowTicks + (IsArmed ? dueTime.Ticks : 0);
                _periodTicks = period == Timeout.InfiniteTimeSpan ? 0 : period.Ticks;
            }

            return true;
        }

        public void Fire()
        {
            lock (_owner._gate)
            {
                if (_periodTicks > 0)
                {
                    DueTicks += _periodTicks;
                }
                else
                {
                    IsArmed = false;
                }
            }

            _callback(_state);
        }

        public void Dispose()
        {
            lock (_owner._gate)
            {
                IsArmed = false;
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
