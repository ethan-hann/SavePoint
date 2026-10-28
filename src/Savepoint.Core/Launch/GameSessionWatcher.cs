using Savepoint.Core.Paths;
using Savepoint.Core.Platform;

namespace Savepoint.Core.Launch;

/// <summary>Follows one play session and says when it is over.</summary>
/// <remarks>
/// <para>
/// A program SavePoint started ends the session when it exits. When a process name is configured, that exit is only the start of
/// the wait: a launcher stub that hands over to the real game exits early, so the session then follows the named process, which has
/// <see cref="GameSessionOptions.Grace"/> from the stub's exit to appear. The list is read from the moment the watcher starts, so
/// a game seen while the stub was still alive needs no grace. The started program itself never counts as that game, even under the
/// same name: a game that exits within <see cref="GameSessionOptions.RelaunchWindow"/> of starting and asks its store to start it again
/// has the grace to come back as a new process, while one that ran longer and is gone was quit and ends the session at once. A started
/// program that was listed under the configured name and exits with code 0 after running at least <see cref="GameSessionOptions.CleanQuitMinimumRun"/>
/// was closed by the player, so it waits only two polling intervals (the look that would list a copy started at once) instead of the grace;
/// an earlier clean exit is a store's relaunch stub, and an exit with an error, so both keep the whole grace. A starter
/// whose real game is a process it started itself (directly or through others, see <see cref="ProcessFamily"/>) is followed through those
/// processes while any of them runs, under whatever name they have; the session then ends when the last one does. A link has no program, so it is poll-only and best effort, and with no
/// process name it can only be ended by <see cref="MarkFinished"/>. A link's grace does not run while the store's own helper for the
/// game's start is listed: it counts from the last look that found the helper.
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
    private readonly SortedSet<int> _followed = [];
    private readonly ProcessFamily _family;
    private readonly IProcessInspector _processes;
    private readonly Func<RunningProcess, bool>? _launcherBusy;
    private IReadOnlyList<(int Pid, DateTimeOffset? Start)> _followedLive = [];
    private bool _disposed;
    private bool _childReleased;
    private int? _earlyExit;
    private IReadOnlyList<int> _ambiguous = [];

    /// <summary>Starts following a session that began now.</summary>
    /// <param name="child">The program SavePoint started, or null for a link; the watcher takes ownership of the handle and releases it on <see cref="Dispose"/>.</param>
    /// <param name="processName">The process name to follow, or null (or blank) for none.</param>
    /// <param name="processes">Where running processes come from.</param>
    /// <param name="os">The OS the process list came from; it decides how names are compared.</param>
    /// <param name="options">Polling interval and grace.</param>
    /// <param name="clock">Time source for the start, the end, and every wait.</param>
    /// <param name="gameFolder">The game's install folder. A process the started program starts is followed only when its program lies inside it; null follows none.</param>
    /// <param name="launcherBusy">
    /// Says whether a listed process is the store's own helper for this game's start. For a link start, the grace does not run while any
    /// such process is listed: it counts from the last look that found one. Null for none.
    /// </param>
    /// <param name="startedAt">
    /// When the session began, taken from <paramref name="clock"/> before the program was started so that nothing the program starts
    /// can be dated before it; the time of this call when null.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">The polling interval is not positive, or the grace or the relaunch window is negative.</exception>
    public GameSessionWatcher(
        IGameProcess? child,
        string? processName,
        IProcessInspector processes,
        OsKind os,
        GameSessionOptions options,
        TimeProvider clock,
        string? gameFolder = null,
        Func<RunningProcess, bool>? launcherBusy = null,
        DateTimeOffset? startedAt = null)
    {
        options.EnsureValid();
        _launcherBusy = launcherBusy;
        _child = child;
        _processName = string.IsNullOrWhiteSpace(processName) ? null : processName.Trim();
        _check = new RunningGameCheck(processes, os);
        _options = options;
        _clock = clock;
        _processes = processes;
        StartedAt = startedAt ?? clock.GetUtcNow();
        _family = new ProcessFamily(StartedAt - ProcessFamily.SessionTolerance, gameFolder, PathSyntax.For(os), os == OsKind.Linux, options.RelaunchWindow);
        _loop = Task.Run(RunAsync);
    }

    /// <summary>When the session began.</summary>
    public DateTimeOffset StartedAt { get; }

    /// <summary>The process id of the program SavePoint started, or null for a link; it stays readable after the program exits.</summary>
    public int? StartedPid => _child?.Pid;

    /// <summary>
    /// Completes once, with how the session ended. It is canceled when the watcher is disposed first, and faulted only if reading
    /// the program's state fails in a way the OS does not normally allow.
    /// </summary>
    public Task<SessionOutcome> Outcome => _outcome.Task;

    /// <summary>
    /// The nonzero exit code of the program SavePoint started while the watcher is still waiting for the game after a quick exit;
    /// null when there is no such wait (the program exited with 0, ran a while, or the game has since shown up).
    /// </summary>
    public int? EarlyExitCode
    {
        get
        {
            lock (_gate)
            {
                return _earlyExit;
            }
        }
    }

    /// <summary>Raised when <see cref="EarlyExitCode"/> changes, on a pool thread.</summary>
    public event EventHandler? EarlyExitChanged;

    /// <summary>True once <see cref="Outcome"/> has completed.</summary>
    public bool IsSettled => _outcome.Task.IsCompleted;

    /// <summary>
    /// True while the program SavePoint started is alive, the named process is running, or a process followed from the started program
    /// still runs, whether or not the session has settled:
    /// a player who said they were done may still have the game open. False once disposed, and false for a link with no process name,
    /// which gives nothing to look at.
    /// </summary>
    public bool IsRunning()
    {
        IReadOnlyList<(int Pid, DateTimeOffset? Start)> followed;
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

            followed = _followedLive;
        }

        if (_processName is not null && _check.Probe(_processName) == NamedProcessState.Running)
        {
            return true;
        }

        return followed.Count > 0 && StillRunning(followed);
    }

    // The process list is re-read: the last look may be older than the player's click, and an id alone may belong to someone else by now.
    private bool StillRunning(IReadOnlyList<(int Pid, DateTimeOffset? Start)> followed)
    {
        try
        {
            return _processes.List().Any(process => followed.Any(f => f.Pid == process.Pid && f.Start == process.StartedAt));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
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
        var readable = false;
        var selfSeen = false;
        var namedSeen = false;
        var heldPastGrace = false;
        IReadOnlyList<(int Pid, DateTimeOffset? Start)> lastDescendants = [];
        var lastFollowing = false;
        DateTimeOffset? firstMiss = null;
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

            // The started program does not count as the game it hands over to: a game that exits and asks its store to start it again
            // leaves a same-named process behind only by being the one that was started.
            var look = _check.Look(name, _child?.Pid);
            var state = look.State;
            // A starter that quit at once and left the real game running as a process it started is followed through that process,
            // whatever it is called. A program that ran a while was quit, and once the named game has been seen it is the one to follow.
            namedSeen |= look.OtherPids.Count > 0;
            // A list that could not be read says nothing, so the last answer stands rather than ending a held session on a guess.
            IReadOnlyList<(int Pid, DateTimeOffset? Start)> descendants;
            bool following;
            if (look.Listed is null)
            {
                (descendants, following) = (lastDescendants, lastFollowing);
            }
            else
            {
                descendants = _child is null ? [] : _family.Update(look.Listed, _child.Pid, !childGone);
                lock (_gate)
                {
                    _ambiguous = _family.AmbiguousHandover;
                }

                following = childGone && !namedSeen && descendants.Count > 0 && graceFrom - StartedAt < _options.RelaunchWindow;
                (lastDescendants, lastFollowing) = (descendants, following);
            }

            lock (_gate)
            {
                _followedLive = following ? descendants : [];
            }

            // Only the configured name counts as having been seen; a followed child just holds the session open past the grace.
            observed |= state == NamedProcessState.Running;
            // The store is still starting a link's game, so the game is not late yet: the grace counts from the last look that found its helper.
            if (_child is null && _launcherBusy is { } busy && look.Listed is { } listed && listed.Any(busy))
            {
                graceFrom = _clock.GetUtcNow();
            }

            readable |= childGone && state != NamedProcessState.Unknown;
            selfSeen |= look.IgnoredMatched;
            if (look.OtherPids.Count > 0 || following)
            {
                lock (_gate)
                {
                    _followed.UnionWith(look.OtherPids);
                    _followed.UnionWith(descendants.Select(d => d.Pid));
                }
            }

            // A store may start the game again after it exits with an error (Steam's restart check does), so the wait is kept; the
            // player is only told meanwhile. A list that could not be read leaves what was said before.
            if (look.Listed is not null)
            {
                var quickError = childGone && !observed && !following && !namedSeen && !selfSeen && graceFrom - StartedAt < _options.RelaunchWindow
                    ? ChildExitCode() : null;
                SetEarlyExit(quickError is 0 ? null : quickError);
            }

            // A game that was running under its own name and quit with code 0 after playing a while was closed by the player: a store that
            // asks for a new copy does so after an error exit, or from a stub that exits within seconds, so only the look that would list
            // a copy started at once is kept, not the whole grace.
            var cleanQuit = childGone && !observed && selfSeen && state == NamedProcessState.Absent && !following && !namedSeen
                && graceFrom - StartedAt >= _options.CleanQuitMinimumRun && ChildExitCode() == 0;
            var wait = cleanQuit ? _options.PollInterval * 2 : _options.Grace;

            // The program's own exit is always needed; the named process only lengthens the wait after it.
            if (childGone && state != NamedProcessState.Running)
            {
                var deadline = graceFrom + wait;
                if (observed && state == NamedProcessState.Absent)
                {
                    Settle(SessionEndKind.NamedProcessEnded);
                    return;
                }

                // The grace ran out with a followed child alive; the session ends when the last one does. One look that finds none may be
                // a misread, so it takes a second, and the end is dated at the first.
                if (heldPastGrace && !following)
                {
                    if (firstMiss is { } missedAt)
                    {
                        Settle(SessionEndKind.NamedProcessEnded, missedAt);
                        return;
                    }

                    firstMiss = _clock.GetUtcNow();
                    await WaitForNextLookAsync(stop, null, null);
                    continue;
                }
                else
                {
                    firstMiss = null;
                }

                // A game that ran for a while under the named process and is gone was quit; only a quick exit may be a restart.
                if (!observed && selfSeen && state == NamedProcessState.Absent && graceFrom - StartedAt >= _options.RelaunchWindow)
                {
                    Settle(SessionEndKind.ChildExited, graceFrom);
                    return;
                }

                if (!observed && following && _clock.GetUtcNow() >= deadline)
                {
                    heldPastGrace = true;
                }
                else if (!observed && _clock.GetUtcNow() >= deadline)
                {
                    // No restart came, so the game ended when the program did, and the wait is not play time. A program that ran a while
                    // before it exited is taken at its word even when its name never matched; the run is real.
                    var ranAWhile = _child is not null && graceFrom - StartedAt >= _options.RelaunchWindow;
                    if (selfSeen || (!readable && ranAWhile))
                    {
                        Settle(SessionEndKind.ChildExited, graceFrom);
                    }
                    else if (!readable)
                    {
                        Settle(SessionEndKind.ProcessListUnreadable);
                    }
                    else if (ranAWhile)
                    {
                        Settle(SessionEndKind.NamedProcessNeverMatched, graceFrom);
                    }
                    else if (ChildExitCode() is not (null or 0))
                    {
                        // Nothing took over after the program failed, so it was the program, not the name, that did not work.
                        Settle(SessionEndKind.ProgramFailedAtStart, graceFrom);
                    }
                    else
                    {
                        Settle(SessionEndKind.NamedProcessNeverAppeared);
                    }

                    return;
                }
            }

            await WaitForNextLookAsync(stop, childGone ? null : childExit, childGone && !observed ? graceFrom + wait : null);
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
        if (childExit is null)
        {
            await delay;
            return;
        }

        // A program that exited after the caller last looked is already complete here, and must cut the wait short, not sit it out.
        await Task.WhenAny(delay, childExit);
        await tick.CancelAsync();
    }

    private void SetEarlyExit(int? code)
    {
        lock (_gate)
        {
            if (_earlyExit == code)
            {
                return;
            }

            _earlyExit = code;
        }

        EarlyExitChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool Settle(SessionEndKind kind, DateTimeOffset? endedAt = null)
    {
        int[] followed;
        IReadOnlyList<int> ambiguous;
        lock (_gate)
        {
            followed = [.. _followed];
            ambiguous = _ambiguous;
        }

        var outcome = new SessionOutcome(kind, StartedAt, endedAt ?? _clock.GetUtcNow(), ChildExitCode())
        {
            StartedPid = _child?.Pid,
            FollowedPids = followed,
            AmbiguousHandoverPids = ambiguous,
        };
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
