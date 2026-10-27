using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Services.Jobs;
using static Savepoint.Core.Tests.Services.Jobs.JobRegistryTestKit;

namespace Savepoint.Core.Tests.Services.Jobs;

public class JobRegistryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Start_returns_the_job_id_while_the_work_is_still_blocked_on_its_first_line()
    {
        var registry = new JobRegistry();
        using var release = new ManualResetEventSlim();

        var start = Task.Run(() => registry.Start(JobKind.Capture, "g", cancellable: true, _ =>
        {
            release.Wait(Generous, Ct);
            return Task.FromResult<JobResult?>(null);
        }), Ct);
        var winner = await Task.WhenAny(start, Task.Delay(TimeSpan.FromSeconds(10), Ct));

        Assert.Same(start, winner);
        Assert.Equal(JobStatus.Running, registry.Get(await start)!.Status);
        release.Set();
        await FinishedAsync(registry, await start);
    }

    [Fact]
    public async Task A_started_job_is_running_and_announces_itself()
    {
        var clock = new ManualTimeProvider();
        var registry = new JobRegistry(clock);
        var events = new EventLog(registry);

        var held = new HeldJob(registry, JobKind.Compact, "skyrim", cancellable: true);
        await registry.WhenDeliveredAsync();

        var job = registry.Get(held.Id)!;
        Assert.Equal(JobKind.Compact, job.Kind);
        Assert.Equal("skyrim", job.GameId);
        Assert.Equal(JobStatus.Running, job.Status);
        Assert.True(job.Cancellable);
        Assert.False(job.CancelRequested);
        Assert.Equal(JobProgress.None, job.Progress);
        Assert.Empty(job.RecentLines);
        Assert.Equal(clock.GetUtcNow(), job.StartedAt);
        Assert.Null(job.FinishedAt);
        Assert.Null(job.Result);
        Assert.Null(job.Error);
        Assert.Equal(1, job.Revision);
        AssertSame(job, Assert.Single(events.All));
        held.Finish();
        await FinishedAsync(registry, held.Id);
    }

    [Fact]
    public async Task A_job_for_every_game_has_no_game_id()
    {
        var registry = new JobRegistry();

        var held = new HeldJob(registry, JobKind.RelocateStorage, gameId: null);

        Assert.Null(registry.Get(held.Id)!.GameId);
        held.Finish();
        await FinishedAsync(registry, held.Id);
    }

    [Fact]
    public async Task Work_that_returns_nothing_ends_succeeded_with_exactly_one_final_event()
    {
        var clock = new ManualTimeProvider();
        var registry = new JobRegistry(clock);
        var events = new EventLog(registry);
        var held = new HeldJob(registry);
        await held.Context;
        clock.Advance(TimeSpan.FromSeconds(3));

        held.Finish();
        var done = await FinishedAsync(registry, held.Id);

        Assert.Equal(JobStatus.Succeeded, done.Status);
        Assert.Equal(clock.GetUtcNow(), done.FinishedAt);
        Assert.Null(done.Result);
        Assert.Null(done.Error);
        var seen = events.For(held.Id);
        Assert.Equal([JobStatus.Running, JobStatus.Succeeded], seen.Select(e => e.Status));
        AssertSame(done, seen[^1]);
        Assert.True(seen[^1].Revision > seen[0].Revision);
    }

    [Fact]
    public async Task The_result_the_work_returns_is_kept_and_judged_per_folder()
    {
        var registry = new JobRegistry();
        var error = ErrorInfo.From(new DiskSpaceException("/x", 10, 1));
        var result = new CaptureResult("p", new HistoryPointId("pt"), [new FolderOutcome(RepoRole.Install), new FolderOutcome(RepoRole.External, error)]);

        var done = await StartAndFinishAsync(registry, result);

        Assert.Equal(JobStatus.Partial, done.Status);
        Assert.Same(result, done.Result);
        Assert.Null(done.Error);
    }

    [Fact]
    public async Task A_result_where_every_folder_failed_fails_the_job_with_the_first_error()
    {
        var registry = new JobRegistry();
        var error = ErrorInfo.From(new DiskSpaceException("/x", 10, 1));
        var result = new CaptureResult("p", null, [new FolderOutcome(RepoRole.Install, error)]);

        var done = await StartAndFinishAsync(registry, result);

        Assert.Equal(JobStatus.Failed, done.Status);
        Assert.Equal(error, done.Error);
    }

    [Fact]
    public async Task A_work_exception_fails_the_job_with_the_mapped_error()
    {
        var registry = new JobRegistry();
        var held = new HeldJob(registry);
        await held.Context;

        held.Throw(new DiskSpaceException("/x", 10, 1));
        var done = await FinishedAsync(registry, held.Id);

        Assert.Equal(JobStatus.Failed, done.Status);
        Assert.Equal("error.diskSpace.low", done.Error!.MessageId);
        Assert.Null(done.Result);
    }

    [Fact]
    public async Task An_unexpected_exception_fails_the_job_as_unknown_with_the_detail()
    {
        var registry = new JobRegistry();

        var id = registry.Start(JobKind.Capture, "g", true, _ => throw new InvalidOperationException("kaboom"));
        var done = await FinishedAsync(registry, id);

        Assert.Equal(JobStatus.Failed, done.Status);
        Assert.Equal("error.unknown", done.Error!.MessageId);
        Assert.Contains("kaboom", done.Error.Detail);
    }

    [Theory]
    [InlineData(JobKind.Launch, "error.launch.canceled")]
    [InlineData(JobKind.Capture, "error.capture.cancelled")]
    [InlineData(JobKind.Compact, "error.compaction.cancelled")]
    [InlineData(JobKind.PackStorage, "error.compaction.cancelled")]
    [InlineData(JobKind.RelocateStorage, "error.storageRelocation.cancelled")]
    public async Task Cancelling_a_job_cancels_its_token_and_ends_it_cancelled_with_the_message_for_its_kind(JobKind kind, string messageId)
    {
        var registry = new JobRegistry();
        var started = new Gate();
        var id = registry.Start(kind, "g", cancellable: true, async context =>
        {
            started.Open();
            await Task.Delay(Timeout.Infinite, context.CancellationToken);
            return null;
        });
        await started.Task.WaitAsync(Generous, Ct);

        var result = await registry.CancelAsync(id);
        var done = await FinishedAsync(registry, id);

        Assert.True(result.Ok);
        Assert.Equal(JobStatus.Cancelled, done.Status);
        Assert.True(done.CancelRequested);
        Assert.Equal(messageId, done.Error!.MessageId);
        Assert.Equal("g", done.Error.Args["gameId"]);
        Assert.Null(done.Result);
    }

    [Fact]
    public async Task A_cancellation_exception_without_a_cancel_request_is_a_failure()
    {
        var registry = new JobRegistry();

        var id = registry.Start(JobKind.Capture, "g", true, _ => throw new OperationCanceledException());
        var done = await FinishedAsync(registry, id);

        Assert.Equal(JobStatus.Failed, done.Status);
        Assert.Equal("error.unknown", done.Error!.MessageId);
    }

    [Fact]
    public async Task A_typed_user_cancellation_from_the_work_ends_the_job_cancelled_with_that_error()
    {
        var registry = new JobRegistry();
        var cancelled = new OperationCancelledByUserException(CancelledOperation.Capture, "g");

        var id = registry.Start(JobKind.Capture, "g", true, _ => throw cancelled);
        var done = await FinishedAsync(registry, id);

        Assert.Equal(JobStatus.Cancelled, done.Status);
        Assert.Equal("error.capture.cancelled", done.Error!.MessageId);
    }

    [Fact]
    public async Task Work_that_finishes_despite_a_cancel_request_keeps_its_own_outcome()
    {
        var registry = new JobRegistry();
        var held = new HeldJob(registry);
        await held.Context;

        registry.Cancel(held.Id);
        held.Finish();
        var done = await FinishedAsync(registry, held.Id);

        Assert.Equal(JobStatus.Succeeded, done.Status);
        Assert.True(done.CancelRequested);
    }

    [Fact]
    public async Task A_cancel_request_raises_one_event_and_a_repeat_changes_nothing()
    {
        var registry = new JobRegistry();
        var held = new HeldJob(registry);
        var context = await held.Context;
        var events = await EventLog.FromNowAsync(registry);

        var first = registry.Cancel(held.Id);
        var second = registry.Cancel(held.Id);
        await registry.WhenDeliveredAsync();

        Assert.Equal(JobCancelOutcome.Requested, first);
        Assert.Equal(JobCancelOutcome.AlreadyRequested, second);
        Assert.True(context.CancellationToken.IsCancellationRequested);
        var seen = Assert.Single(events.For(held.Id));
        Assert.True(seen.CancelRequested);
        Assert.Equal(JobStatus.Running, seen.Status);
        held.Finish();
        await FinishedAsync(registry, held.Id);
    }

    [Fact]
    public async Task Cancelling_returns_while_a_cancel_callback_the_work_registered_is_still_running()
    {
        var registry = new JobRegistry();
        var held = new HeldJob(registry);
        var context = await held.Context;
        var entered = new Gate();
        using var block = new ManualResetEventSlim();
        using var registration = context.CancellationToken.Register(() =>
        {
            entered.Open();
            block.Wait(Generous);
        });

        var cancel = Task.Run(() => registry.Cancel(held.Id), Ct);
        var winner = await Task.WhenAny(cancel, Task.Delay(TimeSpan.FromSeconds(10), Ct));

        Assert.Same(cancel, winner);
        Assert.Equal(JobCancelOutcome.Requested, await cancel);
        Assert.True(context.CancellationToken.IsCancellationRequested);
        await entered.Task.WaitAsync(Generous, Ct);
        block.Set();
        held.Finish();
        await FinishedAsync(registry, held.Id);
    }

    [Fact]
    public async Task A_cancel_callback_that_throws_does_not_break_cancelling()
    {
        var registry = new JobRegistry();
        var held = new HeldJob(registry);
        var context = await held.Context;
        using var registration = context.CancellationToken.Register(() => throw new InvalidOperationException("callback"));

        var outcome = registry.Cancel(held.Id);
        held.Finish();
        var done = await FinishedAsync(registry, held.Id);

        Assert.Equal(JobCancelOutcome.Requested, outcome);
        Assert.True(done.CancelRequested);
    }

    [Fact]
    public async Task The_latest_progress_still_arrives_on_the_real_clock_after_a_burst_of_reports()
    {
        var registry = new JobRegistry();
        var events = new EventLog(registry);
        var held = new HeldJob(registry);
        var context = await held.Context;

        context.Progress.Report(new JobProgress(FilesDone: 1));
        context.Progress.Report(new JobProgress(FilesDone: 2));

        Assert.True(
            SpinWait.SpinUntil(() => events.All.Any(e => e.Status == JobStatus.Running && e.Progress.FilesDone == 2), TimeSpan.FromSeconds(10)),
            "the trailing update never arrived while the job was still running");
        held.Finish();
        await FinishedAsync(registry, held.Id);
    }

    [Fact]
    public async Task Cancelling_an_unknown_job_reports_it_and_still_succeeds()
    {
        var registry = new JobRegistry();
        var events = new EventLog(registry);

        var outcome = registry.Cancel(Guid.NewGuid());
        var result = await registry.CancelAsync(Guid.NewGuid());
        await registry.WhenDeliveredAsync();

        Assert.Equal(JobCancelOutcome.Unknown, outcome);
        Assert.True(result.Ok);
        Assert.Empty(events.All);
    }

    [Fact]
    public async Task Cancelling_a_finished_job_reports_it_and_changes_nothing()
    {
        var registry = new JobRegistry();
        var done = await StartAndFinishAsync(registry);
        var events = await EventLog.FromNowAsync(registry);

        var outcome = registry.Cancel(done.JobId);
        var result = await registry.CancelAsync(done.JobId);
        await registry.WhenDeliveredAsync();

        Assert.Equal(JobCancelOutcome.Finished, outcome);
        Assert.True(result.Ok);
        AssertSame(done, registry.Get(done.JobId));
        Assert.Empty(events.All);
    }

    [Fact]
    public async Task A_job_that_is_not_cancellable_is_left_alone()
    {
        var registry = new JobRegistry();
        var held = new HeldJob(registry, JobKind.ClearData, gameId: null, cancellable: false);
        var context = await held.Context;
        var events = await EventLog.FromNowAsync(registry);

        var outcome = registry.Cancel(held.Id);
        var result = await registry.CancelAsync(held.Id);
        await registry.WhenDeliveredAsync();

        Assert.Equal(JobCancelOutcome.NotCancellable, outcome);
        Assert.True(result.Ok);
        Assert.False(context.CancellationToken.IsCancellationRequested);
        Assert.False(registry.Get(held.Id)!.Cancellable);
        Assert.False(registry.Get(held.Id)!.CancelRequested);
        Assert.Empty(events.All);
        held.Finish();
        await FinishedAsync(registry, held.Id);
    }

    [Fact]
    public void Clearing_data_cannot_be_started_as_cancellable()
    {
        var registry = new JobRegistry();

        Assert.Throws<ArgumentException>(() => registry.Start(JobKind.ClearData, null, cancellable: true, _ => Task.FromResult<JobResult?>(null)));
    }

    [Fact]
    public async Task Progress_replaces_the_previous_report_and_its_path_becomes_a_recent_line()
    {
        var registry = new JobRegistry();
        var held = new HeldJob(registry);
        var context = await held.Context;

        context.Progress.Report(new JobProgress(FilesDone: 1, FilesTotal: 10, CurrentPath: "Data/a.esp"));
        context.Progress.Report(new JobProgress(FilesDone: 2));

        var job = registry.Get(held.Id)!;
        Assert.Equal(new JobProgress(FilesDone: 2), job.Progress);
        Assert.Equal([JobLine.ForPath("Data/a.esp")], job.RecentLines);
        held.Finish();
        await FinishedAsync(registry, held.Id);
    }

    [Fact]
    public async Task A_file_that_stays_current_across_many_reports_is_one_recent_line()
    {
        var registry = new JobRegistry();
        var held = new HeldJob(registry);
        var context = await held.Context;

        // While one file is stored the byte count keeps moving and each report still names that file.
        for (var bytes = 1L; bytes <= 16; bytes++)
        {
            context.Progress.Report(new JobProgress(FilesDone: 1, BytesDone: bytes, CurrentPath: "Data/Web/a.png"));
        }

        context.Progress.Report(new JobProgress(FilesDone: 2, CurrentPath: "Data/Web/b.png"));

        Assert.Equal([JobLine.ForPath("Data/Web/a.png"), JobLine.ForPath("Data/Web/b.png")], registry.Get(held.Id)!.RecentLines);
        held.Finish();
        await FinishedAsync(registry, held.Id);
    }

    [Fact]
    public async Task Two_folders_taking_turns_on_their_own_files_still_show_each_file_once()
    {
        var registry = new JobRegistry();
        var held = new HeldJob(registry);
        var context = await held.Context;

        for (var bytes = 1L; bytes <= 3; bytes++)
        {
            context.Progress.Report(new JobProgress(FilesDone: 1, BytesDone: bytes, CurrentPath: "a.png"));
            context.Progress.Report(new JobProgress(FilesDone: 2, BytesDone: bytes, CurrentPath: "b.sav"));
        }

        Assert.Equal([JobLine.ForPath("a.png"), JobLine.ForPath("b.sav")], registry.Get(held.Id)!.RecentLines);
        held.Finish();
        await FinishedAsync(registry, held.Id);
    }

    [Fact]
    public async Task A_path_added_as_a_line_is_not_added_again_by_the_progress_that_names_it()
    {
        var registry = new JobRegistry();
        var held = new HeldJob(registry);
        var context = await held.Context;

        context.AddPath("a.png");
        context.AddPath("b.png");
        context.Progress.Report(new JobProgress(FilesDone: 2, CurrentPath: "b.png"));
        context.Progress.Report(new JobProgress(FilesDone: 2, BytesDone: 9, CurrentPath: "b.png"));

        Assert.Equal([JobLine.ForPath("a.png"), JobLine.ForPath("b.png")], registry.Get(held.Id)!.RecentLines);
        held.Finish();
        await FinishedAsync(registry, held.Id);
    }

    [Fact]
    public async Task Recent_lines_keep_the_newest_200_oldest_first()
    {
        var registry = new JobRegistry();
        var held = new HeldJob(registry);
        var context = await held.Context;

        for (var i = 1; i <= 150; i++)
        {
            context.Progress.Report(new JobProgress(FilesDone: i, CurrentPath: $"path{i}"));
        }

        for (var i = 151; i <= 250; i++)
        {
            context.AddPath($"path{i}");
        }

        var lines = registry.Get(held.Id)!.RecentLines;
        Assert.Equal(200, lines.Count);
        Assert.Equal(JobLine.ForPath("path51"), lines[0]);
        Assert.Equal(JobLine.ForPath("path250"), lines[^1]);
        Assert.Equal(150, registry.Get(held.Id)!.Progress.FilesDone);
        held.Finish();
        await FinishedAsync(registry, held.Id);
    }

    [Fact]
    public async Task Empty_lines_are_ignored()
    {
        var registry = new JobRegistry();
        var held = new HeldJob(registry);
        var context = await held.Context;

        context.AddPath(string.Empty);
        context.AddPath(null);
        context.Progress.Report(new JobProgress(FilesDone: 1, CurrentPath: string.Empty));

        Assert.Empty(registry.Get(held.Id)!.RecentLines);
        held.Finish();
        await FinishedAsync(registry, held.Id);
    }

    [Fact]
    public async Task A_step_becomes_a_recent_line_with_its_arguments_in_the_order_it_was_written()
    {
        var registry = new JobRegistry();
        var held = new HeldJob(registry);
        var context = await held.Context;

        context.AddStep(JobSteps.CaptureStart, ("profile", "Vanilla"), ("snapshot", "Session"));
        context.Progress.Report(new JobProgress(FilesDone: 1, CurrentPath: "Data/a.esp"));
        context.AddStep(JobSteps.CaptureFolder, ("folder", RepoRole.Install), ("path", "C:/Games/Alpha"));

        Assert.Equal(
            [
                JobLine.ForStep(JobSteps.CaptureStart, ("profile", "Vanilla"), ("snapshot", "Session")),
                JobLine.ForPath("Data/a.esp"),
                JobLine.ForStep(JobSteps.CaptureFolder, ("folder", "install"), ("path", "C:/Games/Alpha")),
            ],
            registry.Get(held.Id)!.RecentLines);
        held.Finish();
        await FinishedAsync(registry, held.Id);
    }

    [Fact]
    public async Task Steps_and_paths_share_the_newest_200_lines()
    {
        var registry = new JobRegistry();
        var held = new HeldJob(registry);
        var context = await held.Context;

        for (var i = 1; i <= 150; i++)
        {
            context.AddPath($"path{i}");
        }

        for (var i = 1; i <= 100; i++)
        {
            context.AddStep(JobSteps.CompactGame, ("game", $"game{i}"));
        }

        var lines = registry.Get(held.Id)!.RecentLines;
        Assert.Equal(200, lines.Count);
        Assert.Equal(JobLine.ForPath("path51"), lines[0]);
        Assert.Equal(JobLine.ForStep(JobSteps.CompactGame, ("game", "game100")), lines[^1]);
        held.Finish();
        await FinishedAsync(registry, held.Id);
    }

    [Fact]
    public async Task A_step_written_after_the_job_ended_is_dropped()
    {
        var registry = new JobRegistry();
        var held = new HeldJob(registry);
        var context = await held.Context;
        context.AddStep(JobSteps.ClearSettings);
        held.Finish();
        var done = await FinishedAsync(registry, held.Id);

        context.AddStep(JobSteps.ClearArtwork);

        Assert.Equal([JobLine.ForStep(JobSteps.ClearSettings)], registry.Get(held.Id)!.RecentLines);
        Assert.Equal(done.Revision, registry.Get(held.Id)!.Revision);
    }

    [Fact]
    public async Task Progress_and_lines_that_arrive_after_the_job_ended_are_dropped()
    {
        var registry = new JobRegistry();
        var held = new HeldJob(registry);
        var context = await held.Context;
        context.Progress.Report(new JobProgress(FilesDone: 5, CurrentPath: "last"));
        held.Finish();
        var done = await FinishedAsync(registry, held.Id);
        var events = await EventLog.FromNowAsync(registry);

        context.Progress.Report(new JobProgress(FilesDone: 99, CurrentPath: "late"));
        context.AddPath("late line");
        await registry.WhenDeliveredAsync();

        AssertSame(done, registry.Get(held.Id));
        Assert.Equal(5, done.Progress.FilesDone);
        Assert.Equal([JobLine.ForPath("last")], done.RecentLines);
        Assert.Empty(events.All);
    }

    [Fact]
    public async Task The_list_holds_running_jobs_first_then_the_five_newest_finished_and_never_prunes_a_running_job()
    {
        var registry = new JobRegistry();
        var runningA = new HeldJob(registry);
        var finished = new List<Guid>();
        for (var i = 0; i < 7; i++)
        {
            finished.Add((await StartAndFinishAsync(registry)).JobId);
        }

        var runningB = new HeldJob(registry);
        await runningA.Context;
        await runningB.Context;

        var listed = registry.List().Select(j => j.JobId).ToList();

        Assert.Equal([runningA.Id, runningB.Id, .. finished.Skip(2)], listed);
        Assert.Null(registry.Get(finished[0]));
        Assert.Null(registry.Get(finished[1]));
        Assert.NotNull(registry.Get(finished[2]));
        Assert.Equal(JobStatus.Running, registry.Get(runningA.Id)!.Status);
        runningA.Finish();
        runningB.Finish();
        await FinishedAsync(registry, runningA.Id);
        await FinishedAsync(registry, runningB.Id);
    }

    [Fact]
    public async Task A_busy_games_finished_jobs_do_not_push_another_games_out_of_the_list()
    {
        var registry = new JobRegistry();
        var quiet = new List<Guid>();
        for (var i = 0; i < 2; i++)
        {
            quiet.Add(await FinishedJobAsync(registry, "quiet"));
        }

        var busy = new List<Guid>();
        for (var i = 0; i < 8; i++)
        {
            busy.Add(await FinishedJobAsync(registry, "busy"));
        }

        var listed = registry.List().Select(j => j.JobId).ToList();

        Assert.Equal(7, listed.Count);
        Assert.Equal(quiet, listed.Where(quiet.Contains));
        Assert.Equal(busy.Skip(3), listed.Where(busy.Contains));
        Assert.Null(registry.Get(busy[0]));
        Assert.NotNull(registry.Get(quiet[0]));
    }

    [Fact]
    public async Task Each_game_keeps_its_own_five_and_they_are_listed_in_the_order_they_finished()
    {
        var registry = new JobRegistry();
        var finished = new List<Guid>();
        for (var i = 0; i < 12; i++)
        {
            finished.Add(await FinishedJobAsync(registry, i % 2 == 0 ? "even" : "odd"));
        }

        // The six of each game are cut to the newest five, and what is left stays in finish order.
        Assert.Equal(finished.Skip(2), registry.List().Select(j => j.JobId));
    }

    [Fact]
    public async Task Finished_jobs_that_span_every_game_are_kept_apart_from_each_games()
    {
        var registry = new JobRegistry();
        var spanning = new List<Guid>();
        for (var i = 0; i < 7; i++)
        {
            spanning.Add(await FinishedJobAsync(registry, null));
        }

        var game = await FinishedJobAsync(registry, "g");

        Assert.Equal([.. spanning.Skip(2), game], registry.List().Select(j => j.JobId));
        Assert.Null(registry.Get(spanning[1]));
    }

    private static Task<Guid> FinishedJobAsync(JobRegistry registry, string? gameId)
    {
        var id = registry.Start(JobKind.Capture, gameId, cancellable: true, _ => Task.FromResult<JobResult?>(null));
        return FinishedAsync(registry, id).ContinueWith(done => done.Result.JobId, TaskScheduler.Default);
    }

    [Fact]
    public async Task A_job_started_for_a_profile_names_it_in_every_snapshot()
    {
        var registry = new JobRegistry();
        var events = new EventLog(registry);
        var profile = new JobProfile("profile-1", "Vanilla");

        var id = registry.Start(JobKind.Capture, "g", cancellable: true, _ => Task.FromResult<JobResult?>(null), profile);
        var done = await FinishedAsync(registry, id);

        Assert.Equal(profile, done.Profile);
        Assert.All(events.For(id), snapshot => Assert.Equal(profile, snapshot.Profile));
    }

    [Fact]
    public async Task A_job_that_works_on_no_profile_names_none()
    {
        var registry = new JobRegistry();

        var done = await StartAndFinishAsync(registry);

        Assert.Null(done.Profile);
    }

    [Fact]
    public async Task Lines_that_fall_off_the_front_are_counted_so_a_reader_can_tell_which_lines_are_new()
    {
        var registry = new JobRegistry();
        var held = new HeldJob(registry);
        var context = await held.Context;
        for (var i = 1; i <= 200; i++)
        {
            context.AddPath($"path{i}");
        }

        Assert.Equal(0, registry.Get(held.Id)!.LinesDropped);

        for (var i = 201; i <= 250; i++)
        {
            context.AddPath($"path{i}");
        }

        var job = registry.Get(held.Id)!;
        Assert.Equal(50, job.LinesDropped);
        Assert.Equal(JobLine.ForPath("path51"), job.RecentLines[0]);
        held.Finish();
        await FinishedAsync(registry, held.Id);
    }

    [Fact]
    public async Task A_job_holds_far_more_lines_than_its_snapshot_shows_and_all_of_them_while_under_the_limit()
    {
        var registry = new JobRegistry();
        var held = new HeldJob(registry);
        var context = await held.Context;

        for (var i = 1; i <= 5_200; i++)
        {
            context.AddPath($"path{i}");
        }

        var lines = registry.GetLines(held.Id);
        Assert.Equal(5_200, lines.Count);
        Assert.Equal(JobLine.ForPath("path1"), lines[0]);
        Assert.Equal(JobLine.ForPath("path5200"), lines[^1]);
        Assert.Equal(200, registry.Get(held.Id)!.RecentLines.Count);
        held.Finish();
        await FinishedAsync(registry, held.Id);
    }

    [Fact]
    public async Task A_job_that_writes_more_than_the_limit_keeps_the_newest_lines()
    {
        var registry = new JobRegistry();
        var held = new HeldJob(registry);
        var context = await held.Context;
        var written = JobHistory.MaxLines + 300;

        for (var i = 1; i <= written; i++)
        {
            context.AddPath($"path{i}");
        }

        var lines = registry.GetLines(held.Id);
        Assert.Equal(JobHistory.MaxLines, lines.Count);
        Assert.Equal(JobLine.ForPath("path301"), lines[0]);
        Assert.Equal(JobLine.ForPath($"path{written}"), lines[^1]);
        Assert.Equal(written - 200, registry.Get(held.Id)!.LinesDropped);
        held.Finish();
        await FinishedAsync(registry, held.Id);
    }

    [Fact]
    public async Task A_finished_job_still_gives_its_lines_and_an_unknown_one_gives_none()
    {
        var registry = new JobRegistry();
        var held = new HeldJob(registry);
        var context = await held.Context;
        context.AddPath("a.bin");
        held.Finish();
        await FinishedAsync(registry, held.Id);

        Assert.Equal([JobLine.ForPath("a.bin")], registry.GetLines(held.Id));
        Assert.Empty(registry.GetLines(Guid.NewGuid()));
    }

    [Fact]
    public async Task A_current_path_named_again_after_other_names_pushed_it_out_is_not_added_a_second_time()
    {
        var registry = new JobRegistry();
        var held = new HeldJob(registry);
        var context = await held.Context;
        context.AddPath("Install/last.bin");
        context.Progress.Report(new JobProgress(FilesDone: 1, CurrentPath: "Install/last.bin"));

        // The other folder names more files than the details show, then the first folder reports its bytes with the same current path.
        for (var i = 1; i <= 250; i++)
        {
            context.AddPath($"Saves/s{i}.sav");
        }

        context.Progress.Report(new JobProgress(FilesDone: 251, BytesDone: 9, CurrentPath: "Install/last.bin"));

        Assert.Equal(1, registry.GetLines(held.Id).Count(line => line == JobLine.ForPath("Install/last.bin")));
        Assert.Equal(JobLine.ForPath("Saves/s250.sav"), registry.Get(held.Id)!.RecentLines[^1]);
        held.Finish();
        await FinishedAsync(registry, held.Id);
    }

    [Fact]
    public async Task A_current_path_that_is_still_shown_is_not_added_a_second_time()
    {
        var registry = new JobRegistry();
        var held = new HeldJob(registry);
        var context = await held.Context;
        context.Progress.Report(new JobProgress(FilesDone: 1, CurrentPath: "a.bin"));
        context.Progress.Report(new JobProgress(FilesDone: 2, CurrentPath: "b.bin"));

        context.Progress.Report(new JobProgress(FilesDone: 3, CurrentPath: "a.bin"));

        Assert.Equal([JobLine.ForPath("a.bin"), JobLine.ForPath("b.bin")], registry.Get(held.Id)!.RecentLines);
        held.Finish();
        await FinishedAsync(registry, held.Id);
    }

    [Fact]
    public async Task A_job_that_can_no_longer_be_stopped_says_so_announces_it_and_a_cancel_is_left_alone()
    {
        var registry = new JobRegistry();
        var held = new HeldJob(registry);
        var context = await held.Context;
        var events = await EventLog.FromNowAsync(registry);

        context.NoLongerCancellable();
        await registry.WhenDeliveredAsync();

        var job = registry.Get(held.Id)!;
        Assert.False(job.Cancellable);
        Assert.Equal(2, job.Revision);
        AssertSame(job, Assert.Single(events.All));
        Assert.Equal(JobCancelOutcome.NotCancellable, registry.Cancel(held.Id));
        Assert.False(registry.Get(held.Id)!.CancelRequested);
        Assert.False(context.CancellationToken.IsCancellationRequested);
        held.Finish();
        await FinishedAsync(registry, held.Id);
    }

    // The token of a job that is asked to stop is cancelled a moment after the request is recorded, so a job that reads the token alone can
    // pass its point of no return with a stop already accepted.
    [Fact]
    public async Task A_job_is_told_when_a_stop_was_already_accepted_as_it_says_it_can_no_longer_be_stopped()
    {
        var registry = new JobRegistry();
        var held = new HeldJob(registry);
        var context = await held.Context;
        Assert.Equal(JobCancelOutcome.Requested, registry.Cancel(held.Id));

        var asked = context.NoLongerCancellable();

        Assert.True(asked);
        Assert.True(registry.Get(held.Id)!.CancelRequested);
        held.Throw(new OperationCanceledException(context.CancellationToken));
        var done = await FinishedAsync(registry, held.Id);
        Assert.Equal(JobStatus.Cancelled, done.Status);
    }

    [Fact]
    public async Task A_job_nobody_asked_to_stop_is_told_so_and_a_later_stop_is_refused()
    {
        var registry = new JobRegistry();
        var held = new HeldJob(registry);
        var context = await held.Context;

        var asked = context.NoLongerCancellable();

        Assert.False(asked);
        Assert.Equal(JobCancelOutcome.NotCancellable, registry.Cancel(held.Id));
        held.Finish();
        await FinishedAsync(registry, held.Id);
    }

    [Fact]
    public async Task Saying_a_job_can_no_longer_be_stopped_twice_or_after_it_ended_changes_nothing()
    {
        var registry = new JobRegistry();
        var held = new HeldJob(registry);
        var context = await held.Context;
        context.NoLongerCancellable();
        var once = registry.Get(held.Id)!.Revision;

        context.NoLongerCancellable();
        held.Finish();
        var done = await FinishedAsync(registry, held.Id);
        context.NoLongerCancellable();

        Assert.Equal(2, once);
        Assert.Equal(once + 1, done.Revision);
        Assert.False(done.Cancellable);
    }

    [Fact]
    public async Task A_pruned_job_cannot_be_cancelled_and_is_reported_unknown()
    {
        var registry = new JobRegistry();
        var first = await StartAndFinishAsync(registry);
        for (var i = 0; i < 5; i++)
        {
            await StartAndFinishAsync(registry);
        }

        Assert.Equal(JobCancelOutcome.Unknown, registry.Cancel(first.JobId));
    }

    [Fact]
    public async Task Every_job_reports_growing_revisions_and_exactly_one_final_event_even_when_cancels_race_completion()
    {
        var registry = new JobRegistry();
        var events = new EventLog(registry);
        var ids = new List<Guid>();
        for (var i = 0; i < 200; i++)
        {
            var id = registry.Start(JobKind.Capture, "g", true, context =>
            {
                context.Progress.Report(new JobProgress(FilesDone: 1));
                return Task.FromResult<JobResult?>(null);
            });
            ids.Add(id);
            _ = Task.Run(() => registry.Cancel(id), Ct);
        }

        // Most of the 200 are pruned from the registry by now, so the event log is the only record that they ended.
        Assert.True(SpinWait.SpinUntil(() => ids.All(id => events.For(id).Any(e => e.Status != JobStatus.Running)), Generous));
        await registry.WhenDeliveredAsync();
        foreach (var id in ids)
        {
            var seen = events.For(id);
            Assert.Equal(1, seen.Count(e => e.Status != JobStatus.Running));
            Assert.NotEqual(JobStatus.Running, seen[^1].Status);
            Assert.Equal(seen.Select(e => e.Revision).Order(), seen.Select(e => e.Revision));
            Assert.Equal(seen.Count, seen.Select(e => e.Revision).Distinct().Count());
        }
    }

    [Fact]
    public async Task A_subscriber_that_blocks_does_not_stall_starting_reporting_cancelling_or_finishing()
    {
        var registry = new JobRegistry();
        using var block = new ManualResetEventSlim();
        var entered = new Gate();
        registry.JobChanged += (_, _) =>
        {
            entered.Open();
            block.Wait(Generous);
        };

        var held = new HeldJob(registry);
        await entered.Task.WaitAsync(Generous, Ct);
        var context = await held.Context;
        context.Progress.Report(new JobProgress(FilesDone: 1));
        var outcome = registry.Cancel(held.Id);
        held.Finish();

        Assert.Equal(JobCancelOutcome.Requested, outcome);
        Assert.True(SpinWait.SpinUntil(() => registry.Get(held.Id)!.Status == JobStatus.Succeeded, Generous));
        block.Set();
        await registry.WhenDeliveredAsync();
    }

    [Fact]
    public async Task A_subscriber_that_throws_does_not_hide_events_from_the_others_or_hurt_the_job()
    {
        var registry = new JobRegistry();
        registry.JobChanged += (_, _) => throw new InvalidOperationException("bad subscriber");
        var events = new EventLog(registry);

        var done = await StartAndFinishAsync(registry);

        Assert.Equal(JobStatus.Succeeded, done.Status);
        Assert.Equal([JobStatus.Running, JobStatus.Succeeded], events.For(done.JobId).Select(e => e.Status));
    }

    [Fact]
    public async Task Jobs_run_independently_and_a_failure_in_one_leaves_the_others_alone()
    {
        var registry = new JobRegistry();
        var healthy = new HeldJob(registry);
        await healthy.Context;

        var failing = registry.Start(JobKind.Capture, "other", true, _ => throw new InvalidOperationException("boom"));
        var failed = await FinishedAsync(registry, failing);

        Assert.Equal(JobStatus.Failed, failed.Status);
        Assert.Equal(JobStatus.Running, registry.Get(healthy.Id)!.Status);
        healthy.Finish();
        await FinishedAsync(registry, healthy.Id);
    }
}
