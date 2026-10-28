using Savepoint.Core.Api;
using static Savepoint.App.Tests.Fakes.FakeAssert;
using static Savepoint.App.Tests.Fakes.SampleScenario;

namespace Savepoint.App.Tests.Fakes;

// The real backend raises its events from its own threads after the call that caused them has returned. The fake's other delivery
// modes let a screen test meet that order instead of the inline one.
public class FakeEventDeliveryTests
{
    [Fact]
    public async Task Inline_delivery_raises_the_event_on_the_calling_thread_before_the_call_returns()
    {
        var backend = FakeBackend.Sample();
        var seen = new List<(JobSnapshot Job, int Thread)>();
        backend.Jobs.JobChanged += (_, job) => seen.Add((job, Environment.CurrentManagedThreadId));

        var started = Ok(await backend.Profiles.CaptureAsync(SkyrimId, ModdedId, "Inline"));

        var raised = Assert.Single(seen);
        Assert.Equal(started.JobId, raised.Job.JobId);
        Assert.Equal(Environment.CurrentManagedThreadId, raised.Thread);
    }

    [Fact]
    public async Task Queued_delivery_holds_the_event_until_it_is_asked_for_and_then_keeps_the_order()
    {
        var backend = FakeBackend.Sample();
        backend.Delivery = EventDelivery.Queued;
        var seen = new List<JobSnapshot>();
        backend.Jobs.JobChanged += (_, job) => seen.Add(job);

        var started = Ok(await backend.Profiles.CaptureAsync(SkyrimId, ModdedId, "Queued"));
        backend.Jobs.ReportProgress(started.JobId, new JobProgress(FilesDone: 1));
        backend.Jobs.Complete(started.JobId);

        Assert.Empty(seen);
        backend.DeliverQueuedEvents();
        Assert.Equal([JobStatus.Running, JobStatus.Running, JobStatus.Succeeded], seen.Select(job => job.Status));
        Assert.Equal([1L, 2L, 3L], seen.Select(job => job.Revision));
    }

    [Fact]
    public async Task Queued_events_are_raised_on_the_thread_that_asks_for_them()
    {
        var backend = FakeBackend.Sample();
        backend.Delivery = EventDelivery.Queued;
        var thread = 0;
        backend.Jobs.JobChanged += (_, _) => thread = Environment.CurrentManagedThreadId;
        Ok(await backend.Profiles.CaptureAsync(SkyrimId, ModdedId, "Queued"));

        backend.DeliverQueuedEvents();

        Assert.Equal(Environment.CurrentManagedThreadId, thread);
    }

    [Fact]
    public async Task Background_delivery_raises_the_events_in_order_on_another_thread_once_the_call_has_returned()
    {
        var backend = FakeBackend.Sample();
        backend.Delivery = EventDelivery.Background;
        var seen = new List<(JobSnapshot Job, int Thread)>();
        backend.Jobs.JobChanged += (_, job) =>
        {
            lock (seen)
            {
                seen.Add((job, Environment.CurrentManagedThreadId));
            }
        };

        var started = Ok(await backend.Profiles.CaptureAsync(SkyrimId, ModdedId, "Background"));
        backend.Jobs.Complete(started.JobId);
        await backend.WhenEventsDeliveredAsync();

        lock (seen)
        {
            Assert.Equal([JobStatus.Running, JobStatus.Succeeded], seen.Select(raised => raised.Job.Status));
            Assert.All(seen, raised => Assert.NotEqual(Environment.CurrentManagedThreadId, raised.Thread));
        }
    }

    [Fact]
    public async Task Background_delivery_reports_a_subscriber_that_threw_when_the_test_waits_for_the_events()
    {
        var backend = FakeBackend.Sample();
        backend.Delivery = EventDelivery.Background;
        backend.Jobs.JobChanged += (_, _) => throw new InvalidOperationException("a subscriber broke");
        Ok(await backend.Profiles.CaptureAsync(SkyrimId, ModdedId, "Background"));

        var failure = await Assert.ThrowsAsync<AggregateException>(backend.WhenEventsDeliveredAsync);

        Assert.Contains("a subscriber broke", failure.InnerExceptions.Single().Message);
    }

    [Fact]
    public async Task A_session_event_follows_the_same_delivery_mode_as_a_job_event()
    {
        var backend = FakeBackend.Sample();
        backend.Delivery = EventDelivery.Queued;
        var seen = new List<LaunchSessionInfo>();
        backend.Launch.SessionChanged += (_, session) => seen.Add(session);

        Ok(await backend.Launch.StartAsync(SkyrimId, new LaunchTarget.ProfileTip(ModdedId)));
        backend.Launch.ReportRunning(SkyrimId);

        Assert.Empty(seen);
        backend.DeliverQueuedEvents();
        Assert.Equal(["Preparing", "Running"], seen.Select(session => session.State.GetType().Name));
    }
}
