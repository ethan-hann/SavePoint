using Savepoint.App.Hosting;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;

namespace Savepoint.App.Tests.Hosting;

// On the way out of a crash, what can be cancelled is asked to stop, so no git process or job goes on while the last window is up.
public class RunningJobsTests
{
    [Fact]
    public async Task Every_running_job_that_can_be_cancelled_is_asked_to_stop_and_the_rest_are_left_alone()
    {
        var backend = FakeBackend.Mockup();
        var save = FakeAssert.Ok(await backend.Profiles.CaptureAsync(MockupScenario.SkyrimId, MockupScenario.ModdedId, "A point"));
        var move = FakeAssert.Ok(await backend.Settings.RelocateStorageAsync(@"E:\SavePoint"));
        var finished = FakeAssert.Ok(await backend.Profiles.CaptureAsync(MockupScenario.SkyrimId, MockupScenario.VanillaId, "Another"));
        backend.Jobs.Complete(finished.JobId, null);

        RunningJobs.CancelAll(backend.Jobs, TimeSpan.FromSeconds(5));

        var asked = backend.Calls.To<IJobsApi>(nameof(IJobsApi.CancelAsync)).Select(call => call.Args[0]).ToList();
        Assert.Equal([save.JobId, move.JobId], asked);
    }

    [Fact]
    public async Task A_job_that_cannot_be_cancelled_is_not_asked()
    {
        var backend = FakeBackend.Mockup();
        FakeAssert.Ok(await backend.Settings.ClearAllDataAsync());

        RunningJobs.CancelAll(backend.Jobs, TimeSpan.FromSeconds(5));

        Assert.Empty(backend.Calls.To<IJobsApi>(nameof(IJobsApi.CancelAsync)));
    }

    [Fact]
    public void A_cancel_that_never_answers_does_not_hold_the_crash_up_past_the_wait()
    {
        var jobs = new HangingJobs();

        var waited = System.Diagnostics.Stopwatch.StartNew();
        RunningJobs.CancelAll(jobs, TimeSpan.FromMilliseconds(200));
        waited.Stop();

        Assert.Single(jobs.Asked);
        Assert.True(waited.Elapsed < TimeSpan.FromSeconds(10));
    }

    // One running job whose cancel request never completes.
    private sealed class HangingJobs : IJobsApi
    {
        private readonly JobSnapshot _job = new(Guid.NewGuid(), JobKind.Capture, "game", JobStatus.Running, true, false, JobProgress.None, [], DateTimeOffset.UnixEpoch, null, null, null, 1);

        public event EventHandler<JobSnapshot>? JobChanged
        {
            add { }
            remove { }
        }

        public List<Guid> Asked { get; } = [];

        public IReadOnlyList<JobSnapshot> List() => [_job];

        public JobSnapshot? Get(Guid jobId) => _job;

        public Task<OperationResult<Unit>> CancelAsync(Guid jobId)
        {
            Asked.Add(jobId);
            return new TaskCompletionSource<OperationResult<Unit>>().Task;
        }
    }
}
