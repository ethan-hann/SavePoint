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
        var backend = FakeBackend.Sample();
        var save = FakeAssert.Ok(await backend.Profiles.CaptureAsync(SampleScenario.SkyrimId, SampleScenario.ModdedId, "A point"));
        var move = FakeAssert.Ok(await backend.Settings.RelocateStorageAsync(@"E:\SavePoint"));
        var finished = FakeAssert.Ok(await backend.Profiles.CaptureAsync(SampleScenario.SatisfactoryId, SampleScenario.MainFactoryId, "Another"));
        backend.Jobs.Complete(finished.JobId, null);

        // The fake never ends a job on its own, so the short wait is all this call spends waiting.
        RunningJobs.CancelAll(backend.Jobs, TimeSpan.FromMilliseconds(200));

        var asked = backend.Calls.To<IJobsApi>(nameof(IJobsApi.CancelAsync)).Select(call => call.Args[0]).ToList();
        Assert.Equal([save.JobId, move.JobId], asked);
    }

    [Fact]
    public async Task A_job_that_cannot_be_cancelled_is_not_asked()
    {
        var backend = FakeBackend.Sample();
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

    [Fact]
    public void The_call_returns_only_after_a_cancelled_job_has_left_running()
    {
        var jobs = new SlowToStopJobs(TimeSpan.FromMilliseconds(500));

        RunningJobs.CancelAll(jobs, TimeSpan.FromSeconds(5));

        Assert.Single(jobs.Asked);
        Assert.NotEqual(JobStatus.Running, jobs.Get(jobs.Asked[0])!.Status);
    }

    [Fact]
    public void A_job_that_never_leaves_running_holds_the_call_only_for_the_wait()
    {
        var jobs = new SlowToStopJobs(TimeSpan.FromMinutes(5));

        var waited = System.Diagnostics.Stopwatch.StartNew();
        RunningJobs.CancelAll(jobs, TimeSpan.FromMilliseconds(300));
        waited.Stop();

        Assert.Equal(JobStatus.Running, jobs.Get(jobs.Asked[0])!.Status);
        Assert.InRange(waited.Elapsed, TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(10));
    }

    // One running job that takes the cancel request at once and leaves Running only after a delay, as a job unwinding does.
    private sealed class SlowToStopJobs(TimeSpan unwind) : IJobsApi
    {
        private readonly Guid _id = Guid.NewGuid();
        private long _cancelledAt;

        public event EventHandler<JobSnapshot>? JobChanged
        {
            add { }
            remove { }
        }

        public List<Guid> Asked { get; } = [];

        public IReadOnlyList<JobSnapshot> List() => [Get(_id)!];

        public JobSnapshot? Get(Guid jobId)
        {
            var cancelledAt = Interlocked.Read(ref _cancelledAt);
            var left = cancelledAt != 0 && System.Diagnostics.Stopwatch.GetElapsedTime(cancelledAt) >= unwind;
            return new JobSnapshot(_id, JobKind.Capture, "game", null, left ? JobStatus.Cancelled : JobStatus.Running, true, false, JobProgress.None, [], 0, DateTimeOffset.UnixEpoch, null, null, null, 1);
        }

        public Task<OperationResult<Unit>> CancelAsync(Guid jobId)
        {
            Asked.Add(jobId);
            Interlocked.CompareExchange(ref _cancelledAt, System.Diagnostics.Stopwatch.GetTimestamp(), 0);
            return Task.FromResult(OperationResult.Success());
        }
    }

    // One running job whose cancel request never completes.
    private sealed class HangingJobs : IJobsApi
    {
        private readonly JobSnapshot _job = new(Guid.NewGuid(), JobKind.Capture, "game", null, JobStatus.Running, true, false, JobProgress.None, [], 0, DateTimeOffset.UnixEpoch, null, null, null, 1);

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
