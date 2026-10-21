namespace Savepoint.App.Tests.Fakes;

// Time only moves when a test advances it, and timers fire from Advance, in due order, on the calling thread.
public sealed class ManualTimeProvider : TimeProvider
{
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now;

    public ManualTimeProvider(DateTimeOffset? start = null)
    {
        _now = start ?? new DateTimeOffset(2026, 7, 8, 12, 0, 0, TimeSpan.Zero);
    }

    public DateTimeOffset Now => _now;

    public int ActiveTimers => _timers.Count(timer => timer.IsArmed);

    public override DateTimeOffset GetUtcNow() => _now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        _timers.Add(timer);
        return timer;
    }

    public void Advance(TimeSpan by)
    {
        var target = _now + by;
        while (_timers.Where(timer => timer.IsArmed && timer.Due <= target).MinBy(timer => timer.Due) is { } next)
        {
            _now = next.Due;
            next.Fire();
        }

        _now = target;
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        private TimeSpan _period = Timeout.InfiniteTimeSpan;

        public bool IsArmed { get; private set; }

        public DateTimeOffset Due { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            _period = period;
            IsArmed = dueTime != Timeout.InfiniteTimeSpan;
            Due = owner._now + (IsArmed ? dueTime : TimeSpan.Zero);
            return true;
        }

        public void Fire()
        {
            if (_period == Timeout.InfiniteTimeSpan || _period == TimeSpan.Zero)
            {
                IsArmed = false;
            }
            else
            {
                Due += _period;
            }

            callback(state);
        }

        public void Dispose() => IsArmed = false;

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
