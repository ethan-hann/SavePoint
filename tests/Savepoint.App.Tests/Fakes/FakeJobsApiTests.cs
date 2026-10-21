using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using static Savepoint.App.Tests.Fakes.FakeAssert;
using static Savepoint.App.Tests.Fakes.MockupScenario;

namespace Savepoint.App.Tests.Fakes;

public class FakeJobsApiTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_started_job_is_running_with_no_progress_and_announces_itself()
    {
        var backend = FakeBackend.Mockup();
        var events = Collect(backend);

        var id = await StartCapture(backend);

        var job = Job(backend, id);
        Assert.Equal(JobKind.Capture, job.Kind);
        Assert.Equal(SkyrimId, job.GameId);
        Assert.Equal(JobStatus.Running, job.Status);
        Assert.True(job.Cancellable);
        Assert.Equal(JobProgress.None, job.Progress);
        Assert.Empty(job.RecentLines);
        Assert.Equal(ScenarioBuilder.DefaultNow, job.StartedAt);
        Assert.Equal(id, Assert.Single(events).JobId);
    }

    [Fact]
    public async Task Progress_replaces_the_previous_report_and_raises_an_event()
    {
        var backend = FakeBackend.Mockup();
        var id = await StartCapture(backend);
        var events = Collect(backend);

        backend.Jobs.ReportProgress(id, new JobProgress(FilesDone: 1, FilesTotal: 10));
        backend.Jobs.ReportProgress(id, new JobProgress(FilesDone: 2));

        Assert.Equal(new JobProgress(FilesDone: 2), Job(backend, id).Progress);
        Assert.Equal(2, events.Count);
        Assert.Equal(new JobProgress(FilesDone: 1, FilesTotal: 10), events[0].Progress);
    }

    [Fact]
    public async Task Progress_paths_are_kept_as_the_newest_200_recent_lines()
    {
        var backend = FakeBackend.Mockup();
        var id = await StartCapture(backend);

        for (var i = 1; i <= 201; i++)
        {
            backend.Jobs.ReportProgress(id, new JobProgress(FilesDone: i, CurrentPath: $"Data/file{i}.esp"));
        }

        backend.Jobs.ReportProgress(id, new JobProgress(FilesDone: 202));

        var lines = Job(backend, id).RecentLines;
        Assert.Equal(200, lines.Count);
        Assert.Equal("Data/file2.esp", lines[0]);
        Assert.Equal("Data/file201.esp", lines[^1]);
    }

    [Fact]
    public async Task Progress_after_the_job_finished_is_dropped_without_an_event()
    {
        var backend = FakeBackend.Mockup();
        var id = await StartCapture(backend);
        backend.Jobs.Complete(id);
        var events = Collect(backend);

        backend.Jobs.ReportProgress(id, new JobProgress(FilesDone: 99));

        Assert.Empty(events);
        Assert.Equal(JobProgress.None, Job(backend, id).Progress);
    }

    [Fact]
    public async Task Completing_without_a_result_reports_every_folder_of_the_call_as_saved()
    {
        var backend = FakeBackend.Mockup();
        var id = await StartCapture(backend);

        backend.Jobs.Complete(id);

        var job = Job(backend, id);
        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Equal(ScenarioBuilder.DefaultNow, job.FinishedAt);
        var result = Assert.IsType<CaptureResult>(job.Result);
        Assert.Equal(ModdedId, result.ProfileId);
        Assert.NotNull(result.PointId);
        Assert.Equal<RepoRole>([RepoRole.Install, RepoRole.External], result.Folders.Select(f => f.Role));
        Assert.All(result.Folders, f => Assert.True(f.Ok));
        Assert.Null(job.Error);
    }

    [Fact]
    public async Task One_failed_folder_makes_the_job_partial_never_a_success()
    {
        var backend = FakeBackend.Mockup();
        var id = await StartCapture(backend);

        backend.Jobs.Complete(id, HalfSaved);

        var job = Job(backend, id);
        Assert.Equal(JobStatus.Partial, job.Status);
        Assert.Same(HalfSaved, job.Result);
        Assert.Null(job.Error);
    }

    [Fact]
    public async Task Every_folder_failing_fails_the_job_with_the_first_folder_error()
    {
        var backend = FakeBackend.Mockup();
        var id = await StartCapture(backend);
        var lockError = FileInUseError;
        var result = new CaptureResult(
            ModdedId,
            null,
            [new FolderOutcome(RepoRole.Install, lockError), new FolderOutcome(RepoRole.External, SavesOutOfRoomError)]);

        backend.Jobs.Complete(id, result);

        var job = Job(backend, id);
        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Same(lockError, job.Error);
    }

    [Fact]
    public async Task Every_folder_cancelled_reports_the_job_as_cancelled()
    {
        var backend = FakeBackend.Mockup();
        var id = await StartCapture(backend);
        var cancelled = ErrorInfo.From(new OperationCancelledByUserException(CancelledOperation.Capture, SkyrimId));
        var result = new CaptureResult(ModdedId, null, [new FolderOutcome(RepoRole.Install, cancelled), new FolderOutcome(RepoRole.External, cancelled)]);

        backend.Jobs.Complete(id, result);

        Assert.Equal(JobStatus.Cancelled, Job(backend, id).Status);
    }

    [Fact]
    public async Task Optimize_results_are_judged_across_every_game_and_folder()
    {
        var backend = FakeBackend.Mockup();
        var started = await backend.Settings.OptimizeAllStorageAsync();
        var id = Ok(started).JobId;
        var result = new OptimizeResult(
        [
            new GameOptimizeOutcome(SkyrimId, [new FolderOutcome(RepoRole.Install), new FolderOutcome(RepoRole.External)], 3 * Gb),
            new GameOptimizeOutcome(SatisfactoryId, [new FolderOutcome(RepoRole.Install, FileInUseError)], null),
        ]);

        backend.Jobs.Complete(id, result);

        Assert.Equal(JobStatus.Partial, Job(backend, id).Status);
    }

    [Fact]
    public async Task Fail_reports_the_given_error()
    {
        var backend = FakeBackend.Mockup();
        var id = await StartCapture(backend);

        backend.Jobs.Fail(id, SavesOutOfRoomError);

        var job = Job(backend, id);
        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Same(SavesOutOfRoomError, job.Error);
        Assert.Null(job.Result);
    }

    [Fact]
    public async Task Cancel_marks_the_request_and_the_test_decides_when_the_job_ends()
    {
        var backend = FakeBackend.Mockup();
        var id = await StartCapture(backend);
        var events = Collect(backend);

        var cancel = await backend.Jobs.CancelAsync(id);

        Assert.True(cancel.Ok);
        var job = Job(backend, id);
        Assert.Equal(JobStatus.Running, job.Status);
        Assert.True(job.CancelRequested);
        Assert.True(Assert.Single(events).CancelRequested);
    }

    [Fact]
    public async Task CompleteCancelled_ends_the_job_with_the_cancelled_id_for_its_kind()
    {
        var backend = FakeBackend.Mockup();
        var capture = await StartCapture(backend);
        var optimize = Ok(await backend.Games.OptimizeStorageAsync(SkyrimId)).JobId;

        backend.Jobs.CompleteCancelled(capture);
        backend.Jobs.CompleteCancelled(optimize);

        Assert.Equal(JobStatus.Cancelled, Job(backend, capture).Status);
        Assert.Equal("error.capture.cancelled", Job(backend, capture).Error!.MessageId);
        Assert.Equal("error.compaction.cancelled", Job(backend, optimize).Error!.MessageId);
    }

    [Fact]
    public async Task Cancelling_an_unknown_or_finished_job_succeeds_and_changes_nothing()
    {
        var backend = FakeBackend.Mockup();
        var id = await StartCapture(backend);
        backend.Jobs.Complete(id);
        var events = Collect(backend);

        var finished = await backend.Jobs.CancelAsync(id);
        var unknown = await backend.Jobs.CancelAsync(Guid.NewGuid());

        Assert.True(finished.Ok);
        Assert.True(unknown.Ok);
        Assert.False(Job(backend, id).CancelRequested);
        Assert.Empty(events);
    }

    [Fact]
    public async Task Clearing_all_data_cannot_be_cancelled()
    {
        var backend = FakeBackend.Mockup();
        var id = Ok(await backend.Settings.ClearAllDataAsync()).JobId;

        await backend.Jobs.CancelAsync(id);

        var job = Job(backend, id);
        Assert.False(job.Cancellable);
        Assert.False(job.CancelRequested);
    }

    [Fact]
    public async Task A_job_finishes_once_and_scripting_a_second_finish_is_a_test_error()
    {
        var backend = FakeBackend.Mockup();
        var id = await StartCapture(backend);
        backend.Jobs.Complete(id);

        Assert.Throws<InvalidOperationException>(() => backend.Jobs.Fail(id, FileInUseError));
        Assert.Equal(JobStatus.Succeeded, Job(backend, id).Status);
    }

    [Fact]
    public async Task Revision_grows_with_every_change()
    {
        var backend = FakeBackend.Mockup();
        var id = await StartCapture(backend);
        var first = Job(backend, id).Revision;

        backend.Jobs.ReportProgress(id, new JobProgress(FilesDone: 1));
        var second = Job(backend, id).Revision;
        backend.Jobs.Complete(id);
        var third = Job(backend, id).Revision;

        Assert.True(first < second && second < third);
    }

    [Fact]
    public async Task List_holds_running_jobs_then_the_five_most_recently_finished()
    {
        var backend = FakeBackend.Mockup();
        var running = await StartCapture(backend);
        var finished = new List<Guid>();
        for (var i = 0; i < 6; i++)
        {
            var id = await StartCapture(backend);
            backend.Jobs.Complete(id);
            finished.Add(id);
        }

        var list = backend.Jobs.List();

        Assert.Equal<Guid>([running, .. finished.Skip(1)], list.Select(j => j.JobId));
        Assert.Null(backend.Jobs.Get(finished[0]));
        Assert.Equal(7, backend.Jobs.Started.Count);
    }

    [Fact]
    public async Task LastStarted_finds_the_newest_job_of_a_kind()
    {
        var backend = FakeBackend.Mockup();
        var capture = await StartCapture(backend);
        var optimize = Ok(await backend.Games.OptimizeStorageAsync(SkyrimId)).JobId;

        Assert.Equal(optimize, backend.Jobs.LastStarted().JobId);
        Assert.Equal(capture, backend.Jobs.LastStarted(JobKind.Capture).JobId);
    }

    private static async Task<Guid> StartCapture(FakeBackend backend)
    {
        var started = await backend.Profiles.CaptureAsync(SkyrimId, ModdedId, "After the Blackreach run");
        return Ok(started).JobId;
    }

    private static List<JobSnapshot> Collect(FakeBackend backend)
    {
        var events = new List<JobSnapshot>();
        backend.Jobs.JobChanged += (_, job) => events.Add(job);
        return events;
    }
}
