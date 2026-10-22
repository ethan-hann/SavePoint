using Savepoint.Core.Api;
using Savepoint.Core.Services.Jobs;
using static Savepoint.Core.Tests.Services.Jobs.JobRegistryTestKit;

namespace Savepoint.Core.Tests.Services.Jobs;

public class JobRegistryThrottleTests
{
    private static readonly TimeSpan _interval = TimeSpan.FromMilliseconds(50);

    private readonly ManualTimeProvider _clock = new();
    private readonly JobRegistry _registry;
    private readonly EventLog _events;

    public JobRegistryThrottleTests()
    {
        _registry = new JobRegistry(_clock);
        _events = new EventLog(_registry);
    }

    [Fact]
    public async Task A_hundred_reports_at_one_instant_raise_no_event_until_the_interval_passes_and_then_one()
    {
        var held = new HeldJob(_registry);
        var context = await held.Context;

        for (var i = 1; i <= 100; i++)
        {
            context.Progress.Report(new JobProgress(FilesDone: i, FilesTotal: 100));
        }

        await _registry.WhenDeliveredAsync();
        Assert.Equal([JobStatus.Running], _events.All.Select(e => e.Status));
        Assert.Equal(100, _registry.Get(held.Id)!.Progress.FilesDone);

        _clock.Advance(_interval);
        await _registry.WhenDeliveredAsync();

        Assert.Equal(2, _events.All.Count);
        Assert.Equal(new JobProgress(FilesDone: 100, FilesTotal: 100), _events.All[1].Progress);
        Assert.Equal(_registry.Get(held.Id)!.Revision, _events.All[1].Revision);
        held.Finish();
        await FinishedAsync(_registry, held.Id);
    }

    [Fact]
    public async Task A_report_after_a_quiet_interval_goes_out_at_once()
    {
        var held = new HeldJob(_registry);
        var context = await held.Context;
        _clock.Advance(_interval);

        context.Progress.Report(new JobProgress(FilesDone: 1));
        await _registry.WhenDeliveredAsync();

        Assert.Equal(2, _events.All.Count);
        Assert.Equal(1, _events.All[1].Progress.FilesDone);
        held.Finish();
        await FinishedAsync(_registry, held.Id);
    }

    [Fact]
    public async Task A_second_report_inside_the_interval_waits_for_the_timer_and_then_the_next_interval_starts()
    {
        var held = new HeldJob(_registry);
        var context = await held.Context;
        _clock.Advance(_interval);
        context.Progress.Report(new JobProgress(FilesDone: 1));
        _clock.Advance(TimeSpan.FromMilliseconds(10));

        context.Progress.Report(new JobProgress(FilesDone: 2));
        await _registry.WhenDeliveredAsync();
        Assert.Equal(2, _events.All.Count);
        _clock.Advance(TimeSpan.FromMilliseconds(40));
        await _registry.WhenDeliveredAsync();

        Assert.Equal(3, _events.All.Count);
        Assert.Equal(2, _events.All[2].Progress.FilesDone);
        held.Finish();
        await FinishedAsync(_registry, held.Id);
    }

    [Fact]
    public async Task A_second_of_reports_every_millisecond_raises_about_twenty_events_and_ends_on_the_last_report()
    {
        var held = new HeldJob(_registry);
        var context = await held.Context;

        for (var i = 1; i <= 1000; i++)
        {
            context.Progress.Report(new JobProgress(FilesDone: i, FilesTotal: 1000));
            _clock.Advance(TimeSpan.FromMilliseconds(1));
        }

        _clock.Advance(_interval);
        await _registry.WhenDeliveredAsync();

        var progressEvents = _events.All.Skip(1).ToList();
        Assert.InRange(progressEvents.Count, 15, 21);
        Assert.Equal(1000, progressEvents[^1].Progress.FilesDone);
        held.Finish();
        await FinishedAsync(_registry, held.Id);
    }

    [Fact]
    public async Task The_final_event_carries_the_latest_progress_even_when_reports_were_still_being_coalesced()
    {
        var held = new HeldJob(_registry);
        var context = await held.Context;
        context.Progress.Report(new JobProgress(FilesDone: 3, CurrentPath: "a"));
        context.Progress.Report(new JobProgress(FilesDone: 4, CurrentPath: "b"));

        held.Finish();
        var done = await FinishedAsync(_registry, held.Id);

        Assert.Equal(4, done.Progress.FilesDone);
        Assert.Equal([JobLine.ForPath("a"), JobLine.ForPath("b")], done.RecentLines);
        AssertSame(done, _events.All[^1]);
        Assert.Equal(0, _clock.PendingTimers);
        _clock.Advance(TimeSpan.FromSeconds(1));
        await _registry.WhenDeliveredAsync();
        AssertSame(done, _events.All[^1]);
        Assert.Equal(1, _events.All.Count(e => e.Status != JobStatus.Running));
    }

    [Fact]
    public async Task Cancelling_is_never_held_back()
    {
        var held = new HeldJob(_registry);
        await held.Context;

        _registry.Cancel(held.Id);
        await _registry.WhenDeliveredAsync();

        Assert.True(_events.All[^1].CancelRequested);
        Assert.Equal(2, _events.All.Count);
        held.Finish();
        await FinishedAsync(_registry, held.Id);
    }

    [Fact]
    public async Task Lines_are_coalesced_like_progress_and_none_are_lost()
    {
        var held = new HeldJob(_registry);
        var context = await held.Context;

        for (var i = 1; i <= 300; i++)
        {
            context.AddPath($"line{i}");
        }

        await _registry.WhenDeliveredAsync();
        Assert.Single(_events.All);
        _clock.Advance(_interval);
        await _registry.WhenDeliveredAsync();

        Assert.Equal(2, _events.All.Count);
        Assert.Equal(200, _events.All[1].RecentLines.Count);
        Assert.Equal(JobLine.ForPath("line101"), _events.All[1].RecentLines[0]);
        Assert.Equal(JobLine.ForPath("line300"), _events.All[1].RecentLines[^1]);
        held.Finish();
        await FinishedAsync(_registry, held.Id);
    }

    [Fact]
    public async Task Each_job_has_its_own_interval()
    {
        var first = new HeldJob(_registry);
        var second = new HeldJob(_registry, JobKind.Compact);
        var firstContext = await first.Context;
        var secondContext = await second.Context;
        _clock.Advance(_interval);

        firstContext.Progress.Report(new JobProgress(FilesDone: 1));
        secondContext.Progress.Report(new JobProgress(FilesDone: 1));
        await _registry.WhenDeliveredAsync();

        Assert.Equal(2, _events.For(first.Id).Count);
        Assert.Equal(2, _events.For(second.Id).Count);
        first.Finish();
        second.Finish();
        await FinishedAsync(_registry, first.Id);
        await FinishedAsync(_registry, second.Id);
    }
}
