using Savepoint.Core.Launch;
using Savepoint.Core.Platform;
using Savepoint.Core.Tests.Services;

namespace Savepoint.Core.Tests.Launch;

// A process list a test edits between clock steps. An empty list stands for an unreadable one, as the real inspectors report it.
internal sealed class ScriptedInspector : IProcessInspector
{
    // Something is always running on a real machine, so an ordinary answer is never empty.
    public static readonly RunningProcess Bystander = new(1, "explorer", null);

    private IReadOnlyList<RunningProcess> _processes = [Bystander];
    private int _calls;

    public int Calls => Volatile.Read(ref _calls);

    public bool Throws { get; set; }

    public void Set(params RunningProcess[] processes) => _processes = processes;

    public void Unreadable() => _processes = [];

    public IReadOnlyList<RunningProcess> List()
    {
        Interlocked.Increment(ref _calls);
        return Throws ? throw new InvalidOperationException("the process list is gone") : _processes;
    }
}

// A watcher on a manual clock, driven in steps small enough that the polling loop always gets to run between them.
internal sealed class WatcherRig : IDisposable
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(30);
    public static readonly RunningProcess Game = new(500, "Game", null);

    private static readonly TimeSpan _patience = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan _step = TimeSpan.FromSeconds(1);

    private readonly bool _polls;

    private WatcherRig(ScriptedProcess? child, string? name, OsKind os)
    {
        Child = child;
        _polls = name is not null;
        Watcher = new GameSessionWatcher(child, name, Processes, os, new GameSessionOptions { PollInterval = Interval, Grace = Grace }, Clock);
    }

    public ManualTimeProvider Clock { get; } = new();

    public ScriptedInspector Processes { get; } = new();

    public ScriptedProcess? Child { get; }

    public GameSessionWatcher Watcher { get; }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // A program SavePoint started, with an optional process name to follow once it is gone.
    public static async Task<WatcherRig> ProgramAsync(string? name = null, OsKind os = OsKind.Windows)
    {
        var rig = new WatcherRig(new ScriptedProcess(), name, os);
        await rig.ParkedAsync();
        return rig;
    }

    // A link handed to the OS: there is no program, only the process name to look for.
    public static async Task<WatcherRig> LinkAsync(string? name = null, OsKind os = OsKind.Windows)
    {
        var rig = new WatcherRig(null, name, os);
        await rig.ParkedAsync();
        return rig;
    }

    public async Task<SessionOutcome> OutcomeAsync() => await Watcher.Outcome.WaitAsync(_patience, Ct);

    // One second at a time, so the clock never jumps past a wait the loop is in: a timer then fires at its exact due time and the
    // loop reads that time, and every wait in these tests is a whole number of seconds.
    public async Task AdvanceAsync(TimeSpan by)
    {
        if (!_polls)
        {
            Clock.Advance(by);
            return;
        }

        for (var left = by; left > TimeSpan.Zero; left -= _step)
        {
            await ParkedAsync();
            Clock.Advance(left < _step ? left : _step);
        }

        await ParkedAsync();
    }

    // The loop reads the list at once when the program exits, so a poll is the sign that it has noticed.
    public async Task ExitChildAsync(int code = 0)
    {
        var before = Processes.Calls;
        Child!.Exit(code);
        await UntilAsync(() => Watcher.IsSettled || (_polls && Processes.Calls > before));
        await ParkedAsync();
    }

    public async Task<bool> FinishAsync()
    {
        var finished = Watcher.MarkFinished();
        await ParkedAsync();
        return finished;
    }

    // True once the loop has read the list and is waiting for its next turn, or the session is over.
    private async Task ParkedAsync()
    {
        if (_polls)
        {
            await UntilAsync(() => Watcher.IsSettled || Clock.PendingTimers > 0);
        }
    }

    public static async Task UntilAsync(Func<bool> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(_patience);
        for (var turns = 0; !condition(); turns++)
        {
            try
            {
                // The loop under test runs on the thread pool, so most waits end within a few yields.
                if (turns < 200)
                {
                    await Task.Yield();
                }
                else
                {
                    await Task.Delay(1, timeout.Token);
                }
            }
            catch (OperationCanceledException) when (!Ct.IsCancellationRequested)
            {
                throw new TimeoutException("The condition did not come true in time.");
            }
        }
    }

    public void Dispose() => Watcher.Dispose();
}
