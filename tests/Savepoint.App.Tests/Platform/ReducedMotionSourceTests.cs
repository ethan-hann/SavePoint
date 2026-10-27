using Savepoint.App.Platform;
using Savepoint.App.Platform.Motion;
using Savepoint.App.Tests.Platform.Fakes;
using Savepoint.Core.Platform;

namespace Savepoint.App.Tests.Platform;

public class ReducedMotionSourceTests
{
    // A read caused by a window change would start within moments, and only waiting shows that none does.
    private static readonly TimeSpan _noReadWindow = TimeSpan.FromMilliseconds(200);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Animations_are_welcome_until_a_read_says_otherwise()
    {
        var source = new ReducedMotionSource(new ScriptedReader(true));

        Assert.False(source.IsReduced);
    }

    [Fact]
    public async Task A_read_that_asks_for_less_motion_is_applied_and_announced_once()
    {
        var reader = new ScriptedReader(true);
        var source = new ReducedMotionSource(reader);
        var changes = 0;
        source.Changed += (_, _) => changes++;

        await source.RefreshAsync(Ct);

        Assert.True(source.IsReduced);
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task Reading_the_same_answer_again_announces_nothing()
    {
        var source = new ReducedMotionSource(new ScriptedReader(true, true));
        var changes = 0;
        source.Changed += (_, _) => changes++;

        await source.RefreshAsync(Ct);
        await source.RefreshAsync(Ct);

        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task The_preference_can_be_switched_back_and_each_change_is_announced()
    {
        var source = new ReducedMotionSource(new ScriptedReader(true, false, true));
        var seen = new List<bool>();
        source.Changed += (_, _) => seen.Add(source.IsReduced);

        await source.RefreshAsync(Ct);
        await source.RefreshAsync(Ct);
        await source.RefreshAsync(Ct);

        Assert.Equal([true, false, true], seen);
    }

    [Fact]
    public async Task Animations_stay_welcome_when_the_system_does_not_say()
    {
        var source = new ReducedMotionSource(new ScriptedReader((bool?)null));
        var changes = 0;
        source.Changed += (_, _) => changes++;

        await source.RefreshAsync(Ct);

        Assert.False(source.IsReduced);
        Assert.Equal(0, changes);
    }

    [Fact]
    public async Task A_read_that_says_nothing_keeps_the_last_known_answer()
    {
        var source = new ReducedMotionSource(new ScriptedReader(true, null));
        var changes = 0;
        source.Changed += (_, _) => changes++;

        await source.RefreshAsync(Ct);
        await source.RefreshAsync(Ct);

        Assert.True(source.IsReduced);
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task A_reader_that_throws_counts_as_not_saying()
    {
        var source = new ReducedMotionSource(new ThrowingReader());
        var changes = 0;
        source.Changed += (_, _) => changes++;

        var error = await Record.ExceptionAsync(() => source.RefreshAsync(Ct));

        Assert.Null(error);
        Assert.False(source.IsReduced);
        Assert.Equal(0, changes);
    }

    [Fact]
    public async Task A_canceled_refresh_throws_the_cancellation()
    {
        var source = new ReducedMotionSource(new ScriptedReader(true));
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.RefreshAsync(canceled.Token));

        Assert.False(source.IsReduced);
    }

    [Fact]
    public async Task The_read_happens_off_the_calling_thread_and_the_announcement_comes_back_to_it()
    {
        var reader = new ScriptedReader(true);
        var source = new ReducedMotionSource(reader);
        var context = new InlineContext();
        bool? announcedInContext = null;

        // A read that is done before the caller awaits it resumes the caller inline, without a post; the caller's context is current either way.
        source.Changed += (_, _) => announcedInContext = SynchronizationContext.Current == context;

        await Task.Run(
            async () =>
            {
                SynchronizationContext.SetSynchronizationContext(context);
                await source.RefreshAsync(Ct);
            },
            Ct);

        Assert.True(announcedInContext);
        // Thread ids are reused by the pool; the caller's context reaching the read is what an inline read would show.
        Assert.NotSame(context, reader.ReadContext);
    }

    [Fact]
    public async Task Activating_the_window_reads_the_preference_again()
    {
        var reader = new ScriptedReader(true, false);
        var source = new ReducedMotionSource(reader);
        var window = new FakeWindow(new WindowActivity(false, false));
        var changed = new SemaphoreSlim(0);
        source.Changed += (_, _) => changed.Release();
        using var watch = source.RefreshOnActivation(window);

        window.Become(new WindowActivity(true, false));
        Assert.True(await changed.WaitAsync(TimeSpan.FromSeconds(10), Ct));
        window.Become(new WindowActivity(false, false));
        window.Become(new WindowActivity(true, false));
        Assert.True(await changed.WaitAsync(TimeSpan.FromSeconds(10), Ct));

        Assert.False(source.IsReduced);
        Assert.Equal(2, reader.Reads);
    }

    [Fact]
    public async Task Leaving_the_window_or_minimizing_it_reads_nothing()
    {
        var reader = new ScriptedReader(true);
        var source = new ReducedMotionSource(reader);
        var window = new FakeWindow(new WindowActivity(true, false));
        using var watch = source.RefreshOnActivation(window);

        window.Become(new WindowActivity(false, false));
        window.Become(new WindowActivity(false, true));
        await Task.Delay(_noReadWindow, Ct);

        Assert.Equal(0, reader.Reads);
    }

    [Fact]
    public async Task A_stopped_watch_reads_nothing_more()
    {
        var reader = new ScriptedReader(true);
        var source = new ReducedMotionSource(reader);
        var window = new FakeWindow(new WindowActivity(false, false));
        var watch = source.RefreshOnActivation(window);
        watch.Dispose();

        window.Become(new WindowActivity(true, false));
        await Task.Delay(_noReadWindow, Ct);

        Assert.Equal(0, reader.Reads);
    }

    [Fact]
    public async Task A_failing_read_on_activation_goes_unnoticed()
    {
        var source = new ReducedMotionSource(new ThrowingReader());
        var window = new FakeWindow(new WindowActivity(false, false));
        using var watch = source.RefreshOnActivation(window);

        var error = Record.Exception(() => window.Become(new WindowActivity(true, false)));
        await Task.Delay(_noReadWindow, Ct);

        Assert.Null(error);
        Assert.False(source.IsReduced);
    }

    [Fact]
    public async Task Windows_asks_the_system_setting_and_starts_no_program()
    {
        var runner = new FakeProcessRunner();
        var source = ReducedMotionSource.Create(OsKind.Windows, runner, () => false);

        await source.RefreshAsync(Ct);

        Assert.True(source.IsReduced);
        Assert.Empty(runner.Run);
    }

    [Fact]
    public async Task MacOS_asks_defaults()
    {
        var runner = FakeProcessRunner.Printing("1\n");
        var source = ReducedMotionSource.Create(OsKind.MacOS, runner, () => throw new InvalidOperationException("not Windows"));

        await source.RefreshAsync(Ct);

        Assert.True(source.IsReduced);
        Assert.Equal("/usr/bin/defaults", Assert.Single(runner.Run).FileName);
    }

    [Fact]
    public async Task Linux_asks_gsettings()
    {
        var runner = FakeProcessRunner.Printing("false\n");
        var source = ReducedMotionSource.Create(OsKind.Linux, runner, () => throw new InvalidOperationException("not Windows"));

        await source.RefreshAsync(Ct);

        Assert.True(source.IsReduced);
        Assert.Equal("gsettings", Assert.Single(runner.Run).FileName);
    }

    private sealed class ScriptedReader : IReducedMotionReader
    {
        private readonly bool?[] _answers;
        private int _reads;

        public ScriptedReader(params bool?[] answers)
        {
            _answers = answers;
        }

        public int Reads => Volatile.Read(ref _reads);

        public SynchronizationContext? ReadContext { get; private set; }

        public ValueTask<bool?> ReadAsync(CancellationToken cancellationToken)
        {
            ReadContext = SynchronizationContext.Current;
            var index = Interlocked.Increment(ref _reads) - 1;
            return ValueTask.FromResult(_answers[Math.Min(index, _answers.Length - 1)]);
        }
    }

    private sealed class ThrowingReader : IReducedMotionReader
    {
        public ValueTask<bool?> ReadAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("boom");
    }

    private sealed class FakeWindow : IWindowActivitySource
    {
        public FakeWindow(WindowActivity current)
        {
            Current = current;
        }

        public WindowActivity Current { get; private set; }

        public event EventHandler? Changed;

        public void Become(WindowActivity state)
        {
            Current = state;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    // Runs what is posted to it at once, with itself current, so a test can see which context an announcement ran under.
    private sealed class InlineContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
            SetSynchronizationContext(this);
            d(state);
        }
    }
}
