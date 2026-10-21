using Savepoint.Core.Platform;

namespace Savepoint.Core.Launch;

/// <summary>Follows one play session and says when it is over.</summary>
/// <remarks>
/// <para>
/// A program SavePoint started ends the session when it exits. When a process name is configured, that exit is only the start of
/// the wait: a launcher stub that hands over to the real game exits early, so the session then follows the named process, which has
/// <see cref="GameSessionOptions.Grace"/> from the stub's exit to appear. The list is read from the moment the watcher starts, so
/// a game seen while the stub was still alive needs no grace. A link has no program, so it is poll-only and best effort, and with no
/// process name it can only be ended by <see cref="MarkFinished"/>.
/// </para>
/// <para>
/// Everything runs on the thread pool; <see cref="Outcome"/> completes on a pool thread. The members are safe to call from any thread.
/// </para>
/// </remarks>
public sealed class GameSessionWatcher : IDisposable
{
    private readonly object _gate = new();
    private readonly TaskCompletionSource<SessionOutcome> _outcome = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _stop = new();
    private readonly IGameProcess? _child;
    private readonly string? _processName;
    private readonly RunningGameCheck _check;
    private readonly GameSessionOptions _options;
    private readonly TimeProvider _clock;
    private readonly Task _loop;
    private bool _disposed;
    private bool _childReleased;

    /// <summary>Starts following a session that began now.</summary>
    /// <param name="child">The program SavePoint started, or null for a link; the watcher takes ownership of the handle and releases it on <see cref="Dispose"/>.</param>
    /// <param name="processName">The process name to follow, or null (or blank) for none.</param>
    /// <param name="processes">Where running processes come from.</param>
    /// <param name="os">The OS the process list came from; it decides how names are compared.</param>
    /// <param name="options">Polling interval and grace.</param>
    /// <param name="clock">Time source for the start, the end, and every wait.</param>
    /// <exception cref="ArgumentOutOfRangeException">The polling interval is not positive or the grace is negative.</exception>
    public GameSessionWatcher(
        IGameProcess? child,
        string? processName,
        IProcessInspector processes,
        OsKind os,
        GameSessionOptions options,
        TimeProvider clock)
    {
        options.EnsureValid();
        _child = child;
        _processName = string.IsNullOrWhiteSpace(processName) ? null : processName.Trim();
        _check = new RunningGameCheck(processes, os);
        _options = options;
        _clock = clock;
        StartedAt = clock.GetUtcNow();
        _loop = Task.Run(RunAsync);
    }

    /// <summary>When the session began.</summary>
    public DateTimeOffset StartedAt { get; }

    /// <summary>
    /// Completes once, with how the session ended. It is canceled when the watcher is disposed first, and faulted only if reading
    /// the program's state fails in a way the OS does not normally allow.
    /// </summary>
    public Task<SessionOutcome> Outcome => _outcome.Task;

    /// <summary>True once <see cref="Outcome"/> has completed.</summary>
    public bool IsSettled => _outcome.Task.IsCompleted;

    /// <summary>
    /// True while the program SavePoint started is alive or the named process is running, whether or not the session has settled:
    /// a player who said they were done may still have the game open. False once disposed, and false for a link with no process name,
    /// which gives nothing to look at.
    /// </summary>
    public bool IsRunning()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            if (_child is { HasExited: false })
            {
                return true;
            }
        }

        return _processName is not null && _check.Probe(_processName) == NamedProcessState.Running;
    }

    /// <summary>Ends the session now because the player says they are done; the game is left running.</summary>
    /// <returns>True when this call ended the session, false when it had already ended or the watcher was disposed.</returns>
    public bool MarkFinished() => Settle(SessionEndKind.ManuallyFinished);

    /// <summary>Stops following and, once the last read is done, releases the program's handle; the game is not stopped.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _outcome.TrySetCanceled();
        _stop.Cancel();
        _ = _loop.ContinueWith(_ => ReleaseChild(), TaskScheduler.Default);
    }

    private async Task RunAsync()
    {
        var stop = _stop.Token;
        try
        {
            if (_processName is not null)
            {
                await FollowNamedProcessAsync(stop);
            }
            else if (_child is not null)
            {
                await _child.WaitForExitAsync(stop);
                Settle(SessionEndKind.ChildExited);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // Ended by the player or disposed.
        }
        catch (Exception ex)
        {
            _outcome.TrySetException(ex);
        }
    }

    private async Task FollowNamedProcessAsync(CancellationToken stop)
    {
        var name = _processName!;
        var childExit = _child?.WaitForExitAsync(stop);
        var childGone = false;
        var observed = false;
        var graceFrom = StartedAt;

        while (true)
        {
            stop.ThrowIfCancellationRequested();
            if (!childGone && (childExit is null || childExit.IsCompleted))
            {
                childGone = true;
                if (childExit is not null)
                {
                    graceFrom = _clock.GetUtcNow();
                }
            }

            var state = _check.Probe(name);
            observed |= state == NamedProcessState.Running;

            // The program's own exit is always needed; the named process only lengthens the wait after it.
            if (childGone && state != NamedProcessState.Running)
            {
                var deadline = graceFrom + _options.Grace;
                if (observed && state == NamedProcessState.Absent)
                {
                    Settle(SessionEndKind.NamedProcessEnded);
                    return;
                }

                if (!observed && _clock.GetUtcNow() >= deadline)
                {
                    Settle(SessionEndKind.NamedProcessNeverAppeared);
                    return;
                }
            }

            await WaitForNextLookAsync(stop, childExit, childGone && !observed ? graceFrom + _options.Grace : null);
        }
    }

    // Waits one polling interval, or less when the grace ends sooner, and wakes early when the program exits.
    private async Task WaitForNextLookAsync(CancellationToken stop, Task? childExit, DateTimeOffset? graceEnds)
    {
        var wait = _options.PollInterval;
        if (graceEnds is { } end && end - _clock.GetUtcNow() is { } remaining && remaining > TimeSpan.Zero && remaining < wait)
        {
            wait = remaining;
        }

        using var tick = CancellationTokenSource.CreateLinkedTokenSource(stop);
        var delay = Task.Delay(wait, _clock, tick.Token);
        if (childExit is null || childExit.IsCompleted)
        {
            await delay;
            return;
        }

        await Task.WhenAny(delay, childExit);
        await tick.CancelAsync();
    }

    private bool Settle(SessionEndKind kind)
    {
        var outcome = new SessionOutcome(kind, StartedAt, _clock.GetUtcNow(), ChildExitCode());
        if (!_outcome.TrySetResult(outcome))
        {
            return false;
        }

        _stop.Cancel();
        return true;
    }

    private int? ChildExitCode()
    {
        lock (_gate)
        {
            return _child is not null && !_childReleased && _child.HasExited ? _child.ExitCode : null;
        }
    }

    private void ReleaseChild()
    {
        lock (_gate)
        {
            _childReleased = true;
        }

        _child?.Dispose();
    }
}
