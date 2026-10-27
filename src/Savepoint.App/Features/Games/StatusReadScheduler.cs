using Savepoint.App.Features.Common;

namespace Savepoint.App.Features.Games;

/// <summary>
/// Turns a stream of "something changed" signals into few status reads: one after the signals stop for the settle time (or after the
/// longest wait, when they never stop), never two at once, and one more after a read that signals arrived during. Everything runs on
/// the UI thread; the timer only hands its tick to the dispatcher.
/// </summary>
internal sealed class StatusReadScheduler : IDisposable
{
    private readonly TimeProvider _clock;
    private readonly IUiDispatcher _dispatcher;
    private readonly Func<bool> _blocked;
    private readonly Func<Task<bool>> _read;
    private readonly TimeSpan _settle;
    private readonly TimeSpan _longestWait;

    private ITimer? _timer;
    private bool _waiting;
    private DateTimeOffset? _firstSignal;
    private bool _reading;
    private bool _again;
    private bool _disposed;

    /// <param name="clock">Times the waits.</param>
    /// <param name="dispatcher">Brings the timer's tick to the UI thread.</param>
    /// <param name="blocked">True while a read must not start: signals then are dropped, because the work that causes the block reads again when it ends.</param>
    /// <param name="read">Reads the status; answers false when it could not start, so the scheduler tries again after the next wait.</param>
    /// <param name="settle">How long the signals must stop before a read.</param>
    /// <param name="longestWait">The longest a read waits for the signals to stop.</param>
    public StatusReadScheduler(TimeProvider clock, IUiDispatcher dispatcher, Func<bool> blocked, Func<Task<bool>> read, TimeSpan settle, TimeSpan longestWait)
    {
        _clock = clock;
        _dispatcher = dispatcher;
        _blocked = blocked;
        _read = read;
        _settle = settle;
        _longestWait = longestWait;
    }

    /// <summary>Notes that something changed and a read is due once it settles.</summary>
    public void Request()
    {
        if (_disposed || _blocked())
        {
            return;
        }

        if (_reading)
        {
            _again = true;
            return;
        }

        var now = _clock.GetUtcNow();
        _firstSignal ??= now;
        var untilCap = _firstSignal.Value + _longestWait - now;
        var wait = untilCap < _settle ? (untilCap < TimeSpan.Zero ? TimeSpan.Zero : untilCap) : _settle;
        _waiting = true;

        // One timer for the scheduler's life: moving it is cheaper than making a new one for every burst of signals.
        if (_timer is null)
        {
            _timer = _clock.CreateTimer(_ => _dispatcher.Post(() => _ = OnTickAsync()), null, wait, Timeout.InfiniteTimeSpan);
        }
        else
        {
            _timer.Change(wait, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Forgets a read that is waiting; one already running is left to finish.</summary>
    public void Cancel()
    {
        _waiting = false;
        _firstSignal = null;
        _again = false;
        _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Stops the scheduler for good.</summary>
    public void Dispose()
    {
        _disposed = true;
        Cancel();
        _timer?.Dispose();
        _timer = null;
    }

    private async Task OnTickAsync()
    {
        // A tick that was queued before a cancel, or after the read it asked for already ran, has nothing to do.
        if (_disposed || !_waiting)
        {
            return;
        }

        _waiting = false;
        _firstSignal = null;
        if (_blocked())
        {
            return;
        }

        _reading = true;
        bool ran;
        try
        {
            ran = await _read();
        }
        finally
        {
            _reading = false;
        }

        if (_disposed)
        {
            return;
        }

        if (!ran || _again)
        {
            _again = false;
            Request();
        }
    }
}
