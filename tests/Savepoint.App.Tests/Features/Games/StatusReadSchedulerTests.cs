using Savepoint.App.Features.Games;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.Common;

namespace Savepoint.App.Tests.Features.Games;

// The scheduler runs against a manual clock and a dispatcher that holds every post until the test runs it, so the timings and the
// hand-off to the UI thread are both in the test's hands.
public class StatusReadSchedulerTests
{
    private static readonly TimeSpan _settle = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan _cap = TimeSpan.FromSeconds(5);

    private static TimeSpan Ms(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);

    private sealed class Harness : IDisposable
    {
        public Harness()
        {
            Dispatcher = new ManualDispatcher { PostsImmediately = false };
            Scheduler = new StatusReadScheduler(Clock, Dispatcher, () => Blocked, () => ReadBody(), _settle, _cap, Failures.Add);
        }

        public List<Exception> Failures { get; } = [];

        public ManualTimeProvider Clock { get; } = new();

        public ManualDispatcher Dispatcher { get; }

        public StatusReadScheduler Scheduler { get; }

        public bool Blocked { get; set; }

        public int Reads { get; private set; }

        // What a read does; a test swaps it to make a read take time or stay in flight.
        public Func<Task<bool>> ReadBody { get; set; } = () => Task.FromResult(true);

        public void Dispose() => Scheduler.Dispose();

        public void Read() => Reads++;

        // Time passes, then the UI thread gets to run what the timers posted.
        public void Pass(TimeSpan by)
        {
            Clock.Advance(by);
            Dispatcher.RunPosted();
        }
    }

    private static Harness Create()
    {
        var harness = new Harness();
        harness.ReadBody = () =>
        {
            harness.Read();
            return Task.FromResult(true);
        };
        return harness;
    }

    [Fact]
    public void The_Games_screen_settles_for_three_quarters_of_a_second_and_waits_at_most_five()
    {
        Assert.Equal(_settle, GamesScreenViewModel.ChangeSettleTime);
        Assert.Equal(_cap, GamesScreenViewModel.ChangeLongestWait);
    }

    [Fact]
    public void A_signal_is_read_once_the_settle_time_has_passed()
    {
        using var h = Create();

        h.Scheduler.Request();
        h.Pass(_settle - Ms(1));
        Assert.Equal(0, h.Reads);

        h.Pass(Ms(1));

        Assert.Equal(1, h.Reads);
    }

    [Fact]
    public void A_second_signal_moves_the_read_to_a_settle_time_after_it()
    {
        using var h = Create();

        h.Scheduler.Request();
        h.Pass(Ms(500));
        h.Scheduler.Request();
        h.Pass(_settle - Ms(1));
        Assert.Equal(0, h.Reads);

        h.Pass(Ms(1));

        Assert.Equal(1, h.Reads);
    }

    [Fact]
    public void A_signal_every_quarter_second_is_read_once_when_the_longest_wait_runs_out()
    {
        using var h = Create();

        h.Scheduler.Request();
        for (var step = 0; step < 19; step++)
        {
            h.Pass(Ms(250));
            h.Scheduler.Request();
        }

        h.Pass(Ms(249));
        Assert.Equal(0, h.Reads);

        h.Pass(Ms(1));

        Assert.Equal(1, h.Reads);
    }

    [Fact]
    public void The_tick_reaches_the_read_only_through_the_dispatcher()
    {
        using var h = Create();

        h.Scheduler.Request();
        h.Clock.Advance(_settle);

        Assert.Equal(0, h.Reads);
        Assert.Equal(1, h.Dispatcher.PostedPending);

        h.Dispatcher.RunPosted();

        Assert.Equal(1, h.Reads);
    }

    [Fact]
    public void A_tick_posted_before_a_new_signal_does_not_read_early()
    {
        using var h = Create();
        h.Scheduler.Request();
        h.Clock.Advance(_settle);

        h.Scheduler.Request();
        h.Dispatcher.RunPosted();
        Assert.Equal(0, h.Reads);

        h.Pass(_settle);

        Assert.Equal(1, h.Reads);
    }

    [Fact]
    public void Signals_while_a_read_runs_lead_to_one_more_read_after_it_ends()
    {
        using var h = Create();
        var release = new TaskCompletionSource<bool>();
        h.ReadBody = () =>
        {
            h.Read();
            return release.Task;
        };
        h.Scheduler.Request();
        h.Pass(_settle);
        Assert.Equal(1, h.Reads);

        h.Scheduler.Request();
        h.Scheduler.Request();
        Assert.Equal(0, h.Clock.ActiveTimers);
        Assert.Equal(1, h.Reads);

        h.ReadBody = () =>
        {
            h.Read();
            return Task.FromResult(true);
        };
        release.SetResult(true);
        h.Pass(_settle);

        Assert.Equal(2, h.Reads);
    }

    [Fact]
    public void The_next_read_waits_twice_as_long_as_the_last_one_took_even_past_the_longest_wait()
    {
        using var h = Create();
        h.ReadBody = () =>
        {
            h.Read();
            h.Clock.Advance(TimeSpan.FromSeconds(3));
            return Task.FromResult(true);
        };
        h.Scheduler.Request();
        h.Pass(_settle);
        Assert.Equal(1, h.Reads);

        h.Scheduler.Request();
        h.Pass(TimeSpan.FromSeconds(6) - Ms(1));
        Assert.Equal(1, h.Reads);

        h.Pass(Ms(1));

        Assert.Equal(2, h.Reads);
    }

    [Fact]
    public void A_read_that_was_quick_adds_no_wait_beyond_the_settle_time()
    {
        using var h = Create();
        h.Scheduler.Request();
        h.Pass(_settle);

        h.Scheduler.Request();
        h.Pass(_settle);

        Assert.Equal(2, h.Reads);
    }

    [Fact]
    public void A_read_that_throws_is_reported_and_the_next_signal_still_reads()
    {
        using var h = Create();
        h.ReadBody = () => Task.FromException<bool>(new InvalidOperationException("boom"));
        h.Scheduler.Request();
        h.Pass(_settle);

        Assert.Equal("boom", Assert.Single(h.Failures).Message);

        h.ReadBody = () =>
        {
            h.Read();
            return Task.FromResult(true);
        };
        h.Scheduler.Request();
        h.Pass(_settle);

        Assert.Equal(1, h.Reads);
    }

    [Fact]
    public void A_signal_while_reads_are_blocked_starts_no_wait()
    {
        using var h = Create();
        h.Blocked = true;

        h.Scheduler.Request();

        Assert.Equal(0, h.Clock.ActiveTimers);
    }

    [Fact]
    public void A_wait_that_ends_while_reads_are_blocked_reads_nothing()
    {
        using var h = Create();
        h.Scheduler.Request();
        h.Blocked = true;

        h.Pass(_settle);

        Assert.Equal(0, h.Reads);
    }
}
