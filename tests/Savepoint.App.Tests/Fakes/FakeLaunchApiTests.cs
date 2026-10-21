using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using static Savepoint.App.Tests.Fakes.FakeAssert;
using static Savepoint.App.Tests.Fakes.MockupScenario;

namespace Savepoint.App.Tests.Fakes;

public class FakeLaunchApiTests
{
    private static readonly LaunchTarget _moddedTip = new LaunchTarget.ProfileTip(ModdedId);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Start_opens_a_preparing_session_tied_to_a_launch_job()
    {
        var backend = FakeBackend.Mockup();
        var sessions = Collect(backend);

        var started = await backend.Launch.StartAsync(SkyrimId, _moddedTip);

        var session = Session(backend);
        Assert.Equal(ModdedId, session.ProfileId);
        Assert.Equal(_moddedTip, session.Target);
        Assert.False(session.StartedFromOlderPoint);
        Assert.IsType<SessionState.Preparing>(session.State);
        Assert.Equal(Ok(started).JobId, session.LaunchJobId);
        Assert.Equal(JobKind.Launch, Job(backend, session.LaunchJobId).Kind);
        Assert.Same(session, Assert.Single(sessions));
    }

    [Fact]
    public async Task A_second_start_while_a_session_is_open_fails_with_already_running()
    {
        var backend = FakeBackend.Mockup();
        await backend.Launch.StartAsync(SkyrimId, _moddedTip);

        var second = await backend.Launch.StartAsync(SkyrimId, new LaunchTarget.ProfileTip(SurvivalId));

        Assert.Equal("error.launch.alreadyRunning", second.Error!.MessageId);
        Assert.Equal(ModdedId, Session(backend).ProfileId);
    }

    [Fact]
    public async Task A_point_target_launches_the_profile_that_holds_the_point()
    {
        var backend = FakeBackend.Mockup();

        await backend.Launch.StartAsync(SkyrimId, new LaunchTarget.Point(ClearedBlackreach));

        var session = Session(backend);
        Assert.Equal(ModdedId, session.ProfileId);
        Assert.True(session.StartedFromOlderPoint);
    }

    [Fact]
    public async Task Switching_progress_reaches_both_the_session_and_its_job()
    {
        var backend = FakeBackend.Mockup();
        await backend.Launch.StartAsync(SkyrimId, _moddedTip);

        backend.Launch.ReportSwitching(SkyrimId, SwitchProgress);

        var session = Session(backend);
        Assert.Equal(SwitchProgress, Assert.IsType<SessionState.Switching>(session.State).Progress);
        Assert.Equal(SwitchProgress, Job(backend, session.LaunchJobId).Progress);
    }

    [Fact]
    public async Task Running_finishes_the_launch_job_and_stamps_the_profile()
    {
        var backend = FakeBackend.Mockup();
        await backend.Launch.StartAsync(SkyrimId, _moddedTip);

        backend.Launch.ReportRunning(SkyrimId, RunningSince);

        var session = Session(backend);
        Assert.Equal(RunningSince, Assert.IsType<SessionState.Running>(session.State).StartedAt);
        Assert.Equal(JobStatus.Succeeded, Job(backend, session.LaunchJobId).Status);
        var modded = Ok(await backend.Profiles.ListAsync(SkyrimId, Ct)).Single(p => p.Id == ModdedId);
        Assert.Equal(RunningSince, modded.LastLaunchedAt);
    }

    [Fact]
    public async Task Running_defaults_to_the_clock()
    {
        var backend = FakeBackend.Mockup();
        await backend.Launch.StartAsync(SkyrimId, _moddedTip);

        backend.Launch.ReportRunning(SkyrimId);

        Assert.Equal(backend.Clock.Now, Assert.IsType<SessionState.Running>(Session(backend).State).StartedAt);
    }

    [Fact]
    public async Task Row_stats_mark_the_profile_that_is_running()
    {
        var backend = FakeBackend.Mockup();
        await backend.Launch.StartAsync(SkyrimId, _moddedTip);
        backend.Launch.ReportRunning(SkyrimId, RunningSince);

        var stats = Ok(await backend.Profiles.GetRowStatsAsync(SkyrimId, Ct));

        Assert.True(stats.Single(s => s.ProfileId == ModdedId).IsRunning);
        Assert.False(stats.Single(s => s.ProfileId == SurvivalId).IsRunning);
        Assert.Equal(RunningSince, stats.Single(s => s.ProfileId == ModdedId).LastPlayedAt);
    }

    [Fact]
    public async Task Exit_reports_the_session_with_the_choices_for_a_latest_point_start()
    {
        var backend = await RunningModded();

        backend.Launch.ReportExited(SkyrimId, PlayDuration, NewSaveFiles);

        var exited = Assert.IsType<SessionState.Exited>(Session(backend).State);
        Assert.Equal(RunningSince, exited.StartedAt);
        Assert.Equal(PlayDuration, exited.PlayDuration);
        Assert.Equal(NewSaveFiles, exited.NewFileCount);
        Assert.Equal(0, exited.ChangedInstallFileCount);
        Assert.Equal<CompletionChoice>([CompletionChoice.CaptureNew, CompletionChoice.UpdateExisting, CompletionChoice.Skip], exited.Options.Choices);
        Assert.Equal(CompletionChoice.CaptureNew, exited.Options.Default);
        Assert.Equal(BeforeFinalBoss, exited.Options.UpdateTarget!.Id);
        Assert.Equal("Before the final boss", exited.Options.UpdateTarget.Name);
        Assert.Equal(0, exited.Options.NewerPointCount);
    }

    [Fact]
    public async Task Exit_after_an_older_point_offers_the_D14_choices_and_counts_newer_points()
    {
        var backend = FakeBackend.Mockup();
        await backend.Launch.StartAsync(SkyrimId, new LaunchTarget.Point(ClearedBlackreach));
        backend.Launch.ReportRunning(SkyrimId, RunningSince);

        backend.Launch.ReportExited(SkyrimId, PlayDuration, NewSaveFiles);

        var options = Assert.IsType<SessionState.Exited>(Session(backend).State).Options;
        Assert.Equal<CompletionChoice>([CompletionChoice.ForkNewProfile, CompletionChoice.MoveForward, CompletionChoice.Skip], options.Choices);
        Assert.Equal(CompletionChoice.ForkNewProfile, options.Default);
        Assert.Null(options.UpdateTarget);
        Assert.Equal(1, options.NewerPointCount);
    }

    [Fact]
    public async Task Exit_uses_scripted_options_when_given()
    {
        var backend = await RunningModded();
        var options = new PostPlayOptions([CompletionChoice.Skip], CompletionChoice.Skip, null, 0);

        backend.Launch.ReportExited(SkyrimId, PlayDuration, 0, changedInstallFileCount: 4, options: options);

        var exited = Assert.IsType<SessionState.Exited>(Session(backend).State);
        Assert.Same(options, exited.Options);
        Assert.Equal(4, exited.ChangedInstallFileCount);
    }

    [Fact]
    public async Task Exit_before_the_game_runs_is_a_script_error()
    {
        var backend = FakeBackend.Mockup();
        await backend.Launch.StartAsync(SkyrimId, _moddedTip);

        Assert.Throws<InvalidOperationException>(() => backend.Launch.ReportExited(SkyrimId, PlayDuration, 1));
    }

    [Fact]
    public async Task Cancelling_the_switch_fails_the_session_with_the_launch_cancelled_id()
    {
        var backend = FakeBackend.Mockup();
        var job = Ok(await backend.Launch.StartAsync(SkyrimId, _moddedTip)).JobId;
        backend.Launch.ReportSwitching(SkyrimId, SwitchProgress);

        await backend.Jobs.CancelAsync(job);
        backend.Jobs.CompleteCancelled(job);

        var failed = Assert.IsType<SessionState.Failed>(Session(backend).State);
        Assert.Equal("error.launch.canceled", failed.Error.MessageId);
    }

    [Fact]
    public async Task A_failed_launch_fails_both_the_session_and_its_job()
    {
        var backend = FakeBackend.Mockup();
        var job = Ok(await backend.Launch.StartAsync(SkyrimId, _moddedTip)).JobId;

        backend.Launch.FailLaunch(SkyrimId, FileInUseError);

        Assert.Same(FileInUseError, Assert.IsType<SessionState.Failed>(Session(backend).State).Error);
        Assert.Same(FileInUseError, Job(backend, job).Error);
    }

    [Fact]
    public async Task A_new_launch_is_allowed_once_the_previous_one_failed()
    {
        var backend = FakeBackend.Mockup();
        await backend.Launch.StartAsync(SkyrimId, _moddedTip);
        backend.Launch.FailLaunch(SkyrimId, FileInUseError);

        var retry = await backend.Launch.StartAsync(SkyrimId, _moddedTip);

        Assert.True(retry.Ok);
        Assert.IsType<SessionState.Preparing>(Session(backend).State);
    }

    [Fact]
    public async Task Mark_finished_needs_an_open_session_and_leaves_the_exit_to_the_script()
    {
        var backend = FakeBackend.Mockup();

        var none = await backend.Launch.MarkFinishedAsync(SkyrimId);
        await RunningIn(backend);
        var running = await backend.Launch.MarkFinishedAsync(SkyrimId);

        Assert.Equal("error.launch.noActiveSession", none.Error!.MessageId);
        Assert.True(running.Ok);
        Assert.IsType<SessionState.Running>(Session(backend).State);
    }

    [Fact]
    public async Task Completing_while_the_game_runs_fails_with_session_still_running()
    {
        var backend = await RunningModded();

        var result = await backend.Launch.CompleteSessionAsync(SkyrimId, new SessionCompletion.Skip());

        Assert.Equal("error.launch.sessionStillRunning", result.Error!.MessageId);
    }

    [Fact]
    public async Task Completing_without_a_session_fails_with_no_active_session()
    {
        var backend = FakeBackend.Mockup();

        var result = await backend.Launch.CompleteSessionAsync(SkyrimId, new SessionCompletion.Skip());

        Assert.Equal("error.launch.noActiveSession", result.Error!.MessageId);
    }

    [Fact]
    public async Task Skip_completes_the_session_at_once_without_a_job()
    {
        var backend = await ExitedModded();
        var jobsBefore = backend.Jobs.Started.Count;

        var result = await backend.Launch.CompleteSessionAsync(SkyrimId, new SessionCompletion.Skip());

        Assert.True(result.Ok);
        var completed = Assert.IsType<SessionState.Completed>(Session(backend).State);
        Assert.IsType<SessionCompletion.Skip>(completed.Completion);
        Assert.Null(completed.Result);
        Assert.Equal(jobsBefore, backend.Jobs.Started.Count);
    }

    [Fact]
    public async Task Capture_new_runs_a_capture_job_whose_end_completes_the_session()
    {
        var backend = await ExitedModded();
        var completion = new SessionCompletion.CaptureNew("After the Blackreach run");

        await backend.Launch.CompleteSessionAsync(SkyrimId, completion);

        var completing = Assert.IsType<SessionState.Completing>(Session(backend).State);
        Assert.Equal(completion, completing.Completion);
        Assert.Equal(JobKind.Capture, Job(backend, completing.JobId).Kind);

        backend.Jobs.Complete(completing.JobId, HalfSaved);

        var completed = Assert.IsType<SessionState.Completed>(Session(backend).State);
        Assert.Same(HalfSaved, completed.Result);
        Assert.Null(completed.Error);
    }

    [Fact]
    public async Task A_failed_post_play_capture_completes_the_session_with_its_error()
    {
        var backend = await ExitedModded();
        await backend.Launch.CompleteSessionAsync(SkyrimId, new SessionCompletion.UpdateExisting("Updated after playing"));
        var job = Assert.IsType<SessionState.Completing>(Session(backend).State).JobId;

        backend.Jobs.Fail(job, SavesOutOfRoomError);

        var completed = Assert.IsType<SessionState.Completed>(Session(backend).State);
        Assert.Null(completed.Result);
        Assert.Same(SavesOutOfRoomError, completed.Error);
    }

    [Fact]
    public async Task Fork_creates_the_new_profile_and_captures_into_it()
    {
        var backend = FakeBackend.Mockup();
        await backend.Launch.StartAsync(SkyrimId, new LaunchTarget.Point(ClearedBlackreach));
        backend.Launch.ReportRunning(SkyrimId, RunningSince);
        backend.Launch.ReportExited(SkyrimId, PlayDuration, NewSaveFiles);

        await backend.Launch.CompleteSessionAsync(SkyrimId, new SessionCompletion.ForkNewProfile("Blackreach again", "Second try"));
        var job = Assert.IsType<SessionState.Completing>(Session(backend).State).JobId;
        backend.Jobs.Complete(job);

        var fork = Ok(await backend.Profiles.ListAsync(SkyrimId, Ct)).Single(p => p.Name == "Blackreach again");
        var result = Assert.IsType<SessionState.Completed>(Session(backend).State).Result!;
        Assert.Equal(fork.Id, result.ProfileId);
    }

    [Fact]
    public async Task A_finished_session_stays_visible_until_the_next_launch_of_that_game()
    {
        var backend = await ExitedModded();
        await backend.Launch.CompleteSessionAsync(SkyrimId, new SessionCompletion.Skip());

        Assert.IsType<SessionState.Completed>(Session(backend).State);
        Assert.Single(backend.Launch.ListSessions());

        await backend.Launch.StartAsync(SkyrimId, new LaunchTarget.ProfileTip(SurvivalId));

        Assert.Equal(SurvivalId, Assert.Single(backend.Launch.ListSessions()).ProfileId);
    }

    [Fact]
    public async Task Every_session_change_raises_an_event_with_a_growing_revision()
    {
        var backend = FakeBackend.Mockup();
        var sessions = Collect(backend);

        await backend.Launch.StartAsync(SkyrimId, _moddedTip);
        backend.Launch.ReportSwitching(SkyrimId, SwitchProgress);
        backend.Launch.ReportRunning(SkyrimId, RunningSince);
        backend.Launch.ReportExited(SkyrimId, PlayDuration, NewSaveFiles);

        Assert.Equal<Type>(
            [typeof(SessionState.Preparing), typeof(SessionState.Switching), typeof(SessionState.Running), typeof(SessionState.Exited)],
            sessions.Select(s => s.State.GetType()));
        Assert.Equal(sessions.Select(s => s.Revision).Order(), sessions.Select(s => s.Revision));
        Assert.Equal(sessions.Count, sessions.Select(s => s.Revision).Distinct().Count());
    }

    [Fact]
    public async Task Preview_returns_the_scripted_preview_and_fails_for_one_never_scripted()
    {
        var backend = FakeBackend.Mockup();

        var scripted = await backend.Launch.GetPreviewAsync(SkyrimId, new LaunchTarget.ProfileTip(SurvivalId), Ct);
        var unscripted = await backend.Launch.GetPreviewAsync(SkyrimId, new LaunchTarget.Save(SurvivalId, FirstNight), Ct);

        Assert.Equal(SurvivalId, Ok(scripted).ProfileId);
        Assert.False(unscripted.Ok);
    }

    [Fact]
    public async Task Discarding_a_folder_clears_its_unsaved_changes()
    {
        var backend = FakeBackend.Mockup();
        var survivalTip = new LaunchTarget.ProfileTip(SurvivalId);

        var result = await backend.Launch.DiscardChangesAsync(SkyrimId, RepoRole.External);

        Assert.True(result.Ok);
        var stats = Ok(await backend.Profiles.GetRowStatsAsync(SkyrimId, Ct));
        Assert.Empty(stats.Single(s => s.ProfileId == SurvivalId).UnsavedChanges);
        var external = Ok(await backend.Launch.GetPreviewAsync(SkyrimId, survivalTip, Ct)).External!;
        Assert.False(external.Dirty);
        Assert.Equal(0, external.DirtyFileCount);
    }

    private static async Task<FakeBackend> RunningModded()
    {
        var backend = FakeBackend.Mockup();
        await RunningIn(backend);
        return backend;
    }

    private static async Task RunningIn(FakeBackend backend)
    {
        await backend.Launch.StartAsync(SkyrimId, _moddedTip);
        backend.Launch.ReportRunning(SkyrimId, RunningSince);
    }

    private static async Task<FakeBackend> ExitedModded()
    {
        var backend = await RunningModded();
        backend.Launch.ReportExited(SkyrimId, PlayDuration, NewSaveFiles);
        return backend;
    }

    private static List<LaunchSessionInfo> Collect(FakeBackend backend)
    {
        var sessions = new List<LaunchSessionInfo>();
        backend.Launch.SessionChanged += (_, session) => sessions.Add(session);
        return sessions;
    }
}
