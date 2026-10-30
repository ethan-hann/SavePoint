using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using static Savepoint.App.Tests.Fakes.FakeAssert;
using static Savepoint.App.Tests.Fakes.SampleScenario;

namespace Savepoint.App.Tests.Fakes;

public class FakeJobsApiTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_started_job_is_running_with_no_progress_and_announces_itself()
    {
        var backend = FakeBackend.Sample();
        var events = Collect(backend);

        var id = await StartCapture(backend);

        var job = Job(backend, id);
        Assert.Equal(JobKind.Capture, job.Kind);
        Assert.Equal(SkyrimId, job.GameId);
        Assert.Equal(JobStatus.Running, job.Status);
        Assert.True(job.Cancellable);
        Assert.Equal(JobProgress.None, job.Progress);
        Assert.Equal(JobSteps.CaptureStart, Assert.IsType<JobLine.Step>(Assert.Single(job.RecentLines)).MessageId);
        Assert.Equal(ScenarioBuilder.DefaultNow, job.StartedAt);
        Assert.Equal(id, Assert.Single(events).JobId);
    }

    [Fact]
    public async Task Progress_paths_are_kept_as_the_newest_200_recent_lines()
    {
        var backend = FakeBackend.Sample();
        var id = await StartCapture(backend);

        for (var i = 1; i <= 201; i++)
        {
            backend.Jobs.ReportProgress(id, new JobProgress(FilesDone: i, CurrentPath: $"Data/file{i}.esp"));
        }

        backend.Jobs.ReportProgress(id, new JobProgress(FilesDone: 202));

        var lines = Job(backend, id).RecentLines;
        Assert.Equal(200, lines.Count);
        Assert.Equal(JobLine.ForPath("Data/file2.esp"), lines[0]);
        Assert.Equal(JobLine.ForPath("Data/file201.esp"), lines[^1]);
    }

    [Fact]
    public async Task A_reported_step_joins_the_progress_paths_as_a_recent_line_and_raises_an_event()
    {
        var backend = FakeBackend.Sample();
        var id = await StartCapture(backend);
        var events = Collect(backend);

        backend.Jobs.ReportProgress(id, new JobProgress(FilesDone: 1, CurrentPath: "Data/a.esp"));
        backend.Jobs.ReportStep(id, JobSteps.CaptureFolderDone, ("folder", RepoRole.Install), ("files", 1));

        Assert.Equal(
            [JobLine.ForPath("Data/a.esp"), JobLine.ForStep(JobSteps.CaptureFolderDone, ("folder", "install"), ("files", 1))],
            Job(backend, id).RecentLines.Skip(1));
        Assert.Equal(2, events.Count);
    }

    [Fact]
    public async Task A_step_for_a_job_that_already_finished_is_dropped_without_an_event()
    {
        var backend = FakeBackend.Sample();
        var id = await StartCapture(backend);
        backend.Jobs.Complete(id);
        var events = Collect(backend);
        var linesBefore = Job(backend, id).RecentLines;

        backend.Jobs.ReportStep(id, JobSteps.CaptureFolderDone, ("folder", RepoRole.Install), ("files", 1));

        Assert.Empty(events);
        Assert.Equal(linesBefore, Job(backend, id).RecentLines);
    }

    [Fact]
    public async Task Every_job_starts_with_the_step_the_real_work_writes_first_so_its_details_are_never_empty()
    {
        var backend = FakeBackend.Sample();
        var modded = Ok(await backend.Profiles.ListAsync(SkyrimId, Ct)).Single(p => p.Id == ModdedId).Name;

        var capture = await StartCapture(backend);
        var one = Ok(await backend.Games.OptimizeStorageAsync(SkyrimId)).JobId;
        var all = Ok(await backend.Settings.OptimizeAllStorageAsync()).JobId;
        var move = Ok(await backend.Settings.RelocateStorageAsync(@"D:\SavePoint")).JobId;
        var clear = Ok(await backend.Settings.ClearAllDataAsync()).JobId;

        Assert.Equal([JobLine.ForStep(JobSteps.CaptureStart, ("profile", modded), ("snapshot", "After the Blackreach run"))], Job(backend, capture).RecentLines);
        Assert.Equal([JobLine.ForStep(JobSteps.CompactGame, ("game", "Skyrim Special Edition"))], Job(backend, one).RecentLines);
        Assert.Equal(3, Job(backend, all).RecentLines.Count(line => line is JobLine.Step { MessageId: JobSteps.CompactGame }));
        Assert.Equal(JobSteps.RelocateStart, Assert.IsType<JobLine.Step>(Assert.Single(Job(backend, move).RecentLines)).MessageId);
        Assert.Equal([JobLine.ForStep(JobSteps.ClearStart, ("games", 3))], Job(backend, clear).RecentLines);
    }

    [Fact]
    public async Task A_snapshot_named_after_its_profile_opens_with_the_first_snapshot_step_like_the_real_work()
    {
        var backend = FakeBackend.Sample();
        var modded = Ok(await backend.Profiles.ListAsync(SkyrimId, Ct)).Single(p => p.Id == ModdedId).Name;

        var capture = Ok(await backend.Profiles.CaptureAsync(SkyrimId, ModdedId, modded)).JobId;

        Assert.Equal([JobLine.ForStep(JobSteps.CaptureStartFirst, ("profile", modded))], Job(backend, capture).RecentLines);
    }

    [Fact]
    public async Task A_launch_says_it_is_starting_the_game_when_it_reaches_a_running_game()
    {
        var backend = FakeBackend.Sample();
        Ok(await backend.Launch.StartAsync(SkyrimId, new LaunchTarget.ProfileTip(ModdedId)));
        var launch = backend.Jobs.LastStarted(JobKind.Launch);

        backend.Launch.ReportRunning(SkyrimId);

        Assert.Equal([JobLine.ForStep(JobSteps.LaunchStarting, ("game", "Skyrim Special Edition"))], Job(backend, launch.JobId).RecentLines);
    }

    [Fact]
    public async Task Progress_after_the_job_finished_is_dropped_without_an_event()
    {
        var backend = FakeBackend.Sample();
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
        var backend = FakeBackend.Sample();
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
        var backend = FakeBackend.Sample();
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
        var backend = FakeBackend.Sample();
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
        var backend = FakeBackend.Sample();
        var id = await StartCapture(backend);
        var cancelled = ErrorInfo.From(new OperationCancelledByUserException(CancelledOperation.Capture, SkyrimId));
        var result = new CaptureResult(ModdedId, null, [new FolderOutcome(RepoRole.Install, cancelled), new FolderOutcome(RepoRole.External, cancelled)]);

        backend.Jobs.Complete(id, result);

        Assert.Equal(JobStatus.Cancelled, Job(backend, id).Status);
    }

    [Fact]
    public async Task Optimize_results_are_judged_across_every_game_and_folder()
    {
        var backend = FakeBackend.Sample();
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
        var backend = FakeBackend.Sample();
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
        var backend = FakeBackend.Sample();
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
        var backend = FakeBackend.Sample();
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
        var backend = FakeBackend.Sample();
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
        var backend = FakeBackend.Sample();
        var id = Ok(await backend.Settings.ClearAllDataAsync()).JobId;

        await backend.Jobs.CancelAsync(id);

        var job = Job(backend, id);
        Assert.False(job.Cancellable);
        Assert.False(job.CancelRequested);
    }

    [Fact]
    public async Task A_job_finishes_once_and_scripting_a_second_finish_is_a_test_error()
    {
        var backend = FakeBackend.Sample();
        var id = await StartCapture(backend);
        backend.Jobs.Complete(id);

        Assert.Throws<InvalidOperationException>(() => backend.Jobs.Fail(id, FileInUseError));
        Assert.Equal(JobStatus.Succeeded, Job(backend, id).Status);
    }

    [Fact]
    public async Task Revision_grows_with_every_change()
    {
        var backend = FakeBackend.Sample();
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
        var backend = FakeBackend.Sample();
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
    public async Task Each_game_keeps_its_own_five_finished_jobs_as_the_real_registry_does()
    {
        var backend = FakeBackend.Sample();
        var quiet = new List<Guid>();
        for (var i = 0; i < 2; i++)
        {
            quiet.Add(Ok(await backend.Games.OptimizeStorageAsync(CaretakerId)).JobId);
            backend.Jobs.Complete(quiet[^1]);
        }

        var busy = new List<Guid>();
        for (var i = 0; i < 8; i++)
        {
            busy.Add(Ok(await backend.Games.OptimizeStorageAsync(SkyrimId)).JobId);
            backend.Jobs.Complete(busy[^1]);
        }

        var listed = backend.Jobs.List().Select(j => j.JobId).ToList();

        Assert.Equal<Guid>([.. quiet, .. busy.Skip(3)], listed);
        Assert.Null(backend.Jobs.Get(busy[0]));
        Assert.NotNull(backend.Jobs.Get(quiet[0]));
    }

    [Fact]
    public async Task Finished_jobs_that_span_every_game_are_kept_apart_from_each_games()
    {
        var backend = FakeBackend.Sample();
        var spanning = new List<Guid>();
        for (var i = 0; i < 7; i++)
        {
            spanning.Add(Ok(await backend.Settings.OptimizeAllStorageAsync()).JobId);
            backend.Jobs.Complete(spanning[^1]);
        }

        var game = Ok(await backend.Games.OptimizeStorageAsync(SkyrimId)).JobId;
        backend.Jobs.Complete(game);

        Assert.Equal<Guid>([.. spanning.Skip(2), game], backend.Jobs.List().Select(j => j.JobId));
    }

    [Fact]
    public async Task Lines_that_fall_off_the_front_are_counted_like_the_real_registry_counts_them()
    {
        var backend = FakeBackend.Sample();
        var id = await StartCapture(backend);

        // The capture's opening step plus 249 paths is 250 lines; the 50 oldest are gone.
        for (var i = 1; i <= 249; i++)
        {
            backend.Jobs.ReportProgress(id, new JobProgress(FilesDone: i, CurrentPath: $"Data/file{i}.esp"));
        }

        var job = Job(backend, id);
        Assert.Equal(200, job.RecentLines.Count);
        Assert.Equal(50, job.LinesDropped);
        Assert.Equal(JobLine.ForPath("Data/file50.esp"), job.RecentLines[0]);
    }

    [Fact]
    public async Task A_snapshot_a_launch_and_the_snapshot_after_play_name_their_profile()
    {
        var backend = FakeBackend.Sample();
        var modded = Ok(await backend.Profiles.ListAsync(SkyrimId, Ct)).Single(p => p.Id == ModdedId);
        var expected = new JobProfile(modded.Id, modded.Name);

        var capture = await StartCapture(backend);
        Ok(await backend.Launch.StartAsync(SkyrimId, new LaunchTarget.ProfileTip(ModdedId)));
        var launch = backend.Jobs.LastStarted(JobKind.Launch);
        backend.Launch.ReportRunning(SkyrimId);
        backend.Launch.ReportExited(SkyrimId, TimeSpan.FromMinutes(5), newFileCount: 1);
        Ok(await backend.Launch.CompleteSessionAsync(SkyrimId, new SessionCompletion.CaptureNew("After play")));
        var completion = backend.Jobs.LastStarted(JobKind.Capture);

        Assert.Equal(expected, Job(backend, capture).Profile);
        Assert.Equal(expected, Job(backend, launch.JobId).Profile);
        Assert.Equal(expected, Job(backend, completion.JobId).Profile);
        Assert.Null(Job(backend, Ok(await backend.Games.OptimizeStorageAsync(SkyrimId)).JobId).Profile);
    }

    [Fact]
    public async Task A_job_scripted_as_no_longer_cancellable_ignores_a_cancel_and_says_so_in_its_snapshot()
    {
        var backend = FakeBackend.Sample();
        var id = Ok(await backend.Settings.RelocateStorageAsync(@"D:\SavePoint")).JobId;
        var events = Collect(backend);

        backend.Jobs.ReportNoLongerCancellable(id);
        var cancel = await backend.Jobs.CancelAsync(id);

        Assert.True(cancel.Ok);
        Assert.False(Job(backend, id).Cancellable);
        Assert.False(Job(backend, id).CancelRequested);
        Assert.Single(events);
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
