using Savepoint.App.Features.Launch;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;

namespace Savepoint.App.Tests.Features.Launch;

public class JobAwaiterTests
{
    private static async Task<Guid> StartCaptureAsync(FakeBackend backend) =>
        FakeAssert.Ok(await backend.Profiles.CaptureAsync(SampleScenario.SkyrimId, SampleScenario.ModdedId, "A point")).JobId;

    [Fact]
    public async Task A_wait_stays_open_while_the_job_runs_and_ends_with_its_final_snapshot()
    {
        var backend = FakeBackend.Sample();
        using var awaiter = new JobAwaiter(backend.Jobs);
        var id = await StartCaptureAsync(backend);

        var wait = awaiter.WaitAsync(id);
        Assert.False(wait.IsCompleted);

        backend.Jobs.ReportProgress(id, new JobProgress(FilesDone: 1));
        Assert.False(wait.IsCompleted);

        backend.Jobs.Complete(id);
        Assert.True(wait.IsCompleted);
        var finished = await wait;

        Assert.Equal(id, finished.JobId);
        Assert.Equal(JobStatus.Succeeded, finished.Status);
    }

    [Fact]
    public async Task A_wait_for_a_job_that_already_ended_completes_at_once()
    {
        var backend = FakeBackend.Sample();
        using var awaiter = new JobAwaiter(backend.Jobs);
        var id = await StartCaptureAsync(backend);
        backend.Jobs.Complete(id);

        var wait = awaiter.WaitAsync(id);

        Assert.True(wait.IsCompleted);
        Assert.Equal(JobStatus.Succeeded, (await wait).Status);
    }

    [Fact]
    public async Task A_cancelled_job_ends_the_wait_with_the_cancelled_snapshot()
    {
        var backend = FakeBackend.Sample();
        using var awaiter = new JobAwaiter(backend.Jobs);
        var id = await StartCaptureAsync(backend);
        var wait = awaiter.WaitAsync(id);

        backend.Jobs.CompleteCancelled(id);

        Assert.True(wait.IsCompleted);
        var finished = await wait;
        Assert.Equal(JobStatus.Cancelled, finished.Status);
        Assert.NotNull(finished.Error);
    }

    [Fact]
    public async Task Several_waiters_on_one_job_all_end_and_other_jobs_do_not_end_them()
    {
        var backend = FakeBackend.Sample();
        using var awaiter = new JobAwaiter(backend.Jobs);
        var first = await StartCaptureAsync(backend);
        var second = await StartCaptureAsync(backend);
        var a = awaiter.WaitAsync(first);
        var b = awaiter.WaitAsync(first);
        var other = awaiter.WaitAsync(second);

        backend.Jobs.Complete(first);

        Assert.True(a.IsCompleted);
        Assert.True(b.IsCompleted);
        Assert.False(other.IsCompleted);
    }

    [Fact]
    public async Task A_failed_job_reports_its_error()
    {
        var backend = FakeBackend.Sample();
        using var awaiter = new JobAwaiter(backend.Jobs);
        var id = await StartCaptureAsync(backend);
        var wait = awaiter.WaitAsync(id);

        backend.Jobs.Fail(id, SampleScenario.SavesOutOfRoomError);

        Assert.True(wait.IsCompleted);
        var finished = await wait;
        Assert.Equal(JobStatus.Failed, finished.Status);
        Assert.Equal(SampleScenario.SavesOutOfRoomError, finished.Error);
    }
}
