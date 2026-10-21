using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Features.Launch;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.Launch;

public class LaunchFlowSessionTests : IDisposable
{
    private readonly FlowRig _f = new();

    public void Dispose() => _f.Dispose();

    private Guid LaunchJob() => _f.Session().LaunchJobId;

    private Guid CaptureJob() => _f.Backend.Jobs.LastStarted(JobKind.Capture).JobId;

    [Fact]
    public async Task The_progress_dialog_follows_the_switch_and_gives_way_to_the_running_card()
    {
        await _f.PlayWithProgressAsync();
        var progress = _f.Progress;
        Assert.True(progress.IsIndeterminate);
        Assert.False(progress.IsClosed);

        _f.Backend.Launch.ReportSwitching(FlowRig.Skyrim, MockupScenario.SwitchProgress);
        Assert.Equal("3.8 GB of 6.1 GB · about 50 seconds left", progress.ProgressText);
        Assert.Equal("62%", progress.PercentText);

        _f.Backend.Launch.ReportRunning(FlowRig.Skyrim, MockupScenario.RunningSince);

        Assert.True(progress.IsClosed);
        Assert.Single(_f.Dialogs.Shown.OfType<SwitchProgressDialogViewModel>());
        Assert.IsType<RunningCardViewModel>(_f.Flow.InlineContentFor(FlowRig.Skyrim));
    }

    [Fact]
    public async Task Cancel_in_the_progress_dialog_cancels_the_launch_job_and_ends_in_a_canceled_card()
    {
        await _f.PlayWithProgressAsync();
        var progress = _f.Progress;
        var job = LaunchJob();

        await progress.CancelCommand.ExecuteAsync(null);

        var cancel = Assert.Single(_f.Backend.Calls.To<IJobsApi>(nameof(IJobsApi.CancelAsync)));
        Assert.Equal([job], cancel.Args);
        Assert.True(progress.IsCancelling);
        Assert.False(progress.IsClosed);

        _f.Backend.Jobs.CompleteCancelled(job);

        Assert.True(progress.IsClosed);
        var card = _f.Inline<ErrorCardViewModel>();
        Assert.Equal("Canceled", card.Title);
        Assert.Equal("Switching was canceled.", card.Body);
        Assert.Equal(["Try again", "Dismiss"], card.Actions.Select(a => a.Label));
        Assert.False(_f.Flow.IsActive(FlowRig.Skyrim));
    }

    [Fact]
    public async Task A_cancel_the_service_refuses_puts_the_dialog_back_and_tells_the_user()
    {
        await _f.PlayWithProgressAsync();
        _f.Backend.FailNext<IJobsApi>(nameof(IJobsApi.CancelAsync), MockupScenario.FolderGoneError);

        await _f.Progress.CancelCommand.ExecuteAsync(null);

        Assert.False(_f.Progress.IsCancelling);
        Assert.Single(_f.Rig.Toasts.Toasts);
    }

    [Fact]
    public async Task A_file_in_use_stops_the_switch_and_try_again_starts_the_same_switch_again()
    {
        await _f.PlayWithProgressAsync();
        var first = _f.Progress;

        _f.Backend.Launch.FailLaunch(FlowRig.Skyrim, MockupScenario.FileInUseError);

        Assert.True(first.IsClosed);
        var card = _f.Inline<ErrorCardViewModel>();
        Assert.Equal("A file is in use, so the switch stopped partway", card.Title);
        Assert.Equal(@"C:\Games\SkyrimSE\Data\Skyrim.esm", card.Path);
        Assert.Equal(["Try again", "Dismiss"], card.Actions.Select(a => a.Label));
        Assert.True(FlowRig.Action(card, "Try again").IsPrimary);
        Assert.False(_f.Flow.IsActive(FlowRig.Skyrim));

        FlowRig.Press(card, "Try again");

        Assert.Equal(2, _f.Starts().Count);
        Assert.Equal(_f.Starts()[0].Args, _f.Starts()[1].Args);
        Assert.Equal(2, _f.Dialogs.Shown.OfType<SwitchProgressDialogViewModel>().Count());
        Assert.False(_f.Progress.IsClosed);
        Assert.Null(_f.Flow.InlineContentFor(FlowRig.Skyrim));
        Assert.True(_f.Flow.IsActive(FlowRig.Skyrim));
    }

    [Fact]
    public async Task A_switch_that_did_not_finish_offers_to_switch_again_first_and_to_review_the_changes()
    {
        await _f.PlayWithProgressAsync();
        _f.Backend.Launch.FailLaunch(FlowRig.Skyrim, ErrorInfo.From(new InterruptedSwitchException(RepoRole.Install, "0123456789abcdef0123456789abcdef01234567")));
        var card = _f.Inline<ErrorCardViewModel>();

        Assert.Equal(["Switch again", "Review the changes", "Dismiss"], card.Actions.Select(a => a.Label));
        Assert.True(FlowRig.Action(card, "Switch again").IsPrimary);
        Assert.False(FlowRig.Action(card, "Review the changes").IsPrimary);

        // Switching again starts the same switch, which is what finishes the unfinished one.
        FlowRig.Press(card, "Switch again");

        Assert.Equal(2, _f.Starts().Count);
        Assert.Equal(_f.Starts()[0].Args, _f.Starts()[1].Args);
        Assert.True(_f.Flow.IsActive(FlowRig.Skyrim));
    }

    [Fact]
    public async Task Reviewing_the_changes_after_a_switch_that_did_not_finish_reads_the_folders_again()
    {
        await _f.PlayModdedAsync();
        _f.Backend.Launch.FailLaunch(FlowRig.Skyrim, ErrorInfo.From(new InterruptedSwitchException(RepoRole.External)));
        var previews = _f.Backend.Calls.To<ILaunchApi>(nameof(ILaunchApi.GetPreviewAsync)).Count;

        FlowRig.Press(_f.Inline<ErrorCardViewModel>(), "Review the changes");

        Assert.Equal(previews + 1, _f.Backend.Calls.To<ILaunchApi>(nameof(ILaunchApi.GetPreviewAsync)).Count);
    }

    [Fact]
    public async Task A_launch_that_fails_with_the_banner_showing_replaces_the_banner_with_the_card()
    {
        await _f.PlayModdedAsync();

        _f.Backend.Launch.FailLaunch(FlowRig.Skyrim, MockupScenario.FileInUseError);

        Assert.Equal("A file is in use, so the switch stopped partway", _f.Inline<ErrorCardViewModel>().Title);
    }

    [Fact]
    public async Task A_game_that_would_not_start_offers_to_edit_the_game_and_to_try_again()
    {
        await _f.PlayModdedAsync();

        _f.Backend.Launch.FailLaunch(FlowRig.Skyrim, FlowRig.Spawn());

        var card = _f.Inline<ErrorCardViewModel>();
        Assert.Equal("The game didn't start", card.Title);
        Assert.Equal(["Edit game", "Try again", "Dismiss"], card.Actions.Select(a => a.Label));
        Assert.True(FlowRig.Action(card, "Edit game").IsPrimary);

        FlowRig.Press(card, "Edit game");

        Assert.Equal([FlowRig.Skyrim], _f.EditRequests);
    }

    [Fact]
    public async Task A_folder_that_is_gone_offers_to_choose_it_in_the_editor()
    {
        await _f.PlayModdedAsync();

        _f.Backend.Launch.FailLaunch(FlowRig.Skyrim, MockupScenario.FolderGoneError);

        var card = _f.Inline<ErrorCardViewModel>();
        Assert.Equal("This game's folder has moved or is gone", card.Title);
        Assert.Equal(["Choose the folder", "Dismiss"], card.Actions.Select(a => a.Label));
        FlowRig.Press(card, "Choose the folder");
        Assert.Equal([FlowRig.Skyrim], _f.EditRequests);
    }

    [Fact]
    public async Task A_folder_the_game_definition_gets_wrong_offers_to_edit_the_game()
    {
        var unconfirmed = ErrorInfo.From(new ConsentMarkerException(ConsentFailure.Missing, MockupScenario.SkyrimInstall, RepoRole.Install, null, null));
        var foreign = ErrorInfo.From(new ConsentMarkerException(ConsentFailure.Conflict, MockupScenario.SkyrimInstall, RepoRole.Install, MockupScenario.SatisfactoryId, RepoRole.Install));
        foreach (var error in new[] { unconfirmed, foreign, MockupScenario.FolderTooBroadError })
        {
            await _f.PlayModdedAsync();
            _f.Backend.Launch.FailLaunch(FlowRig.Skyrim, error);
            var card = _f.Inline<ErrorCardViewModel>();

            Assert.Equal(["Edit game", "Dismiss"], card.Actions.Select(a => a.Label));
            FlowRig.Press(card, "Edit game");
            FlowRig.Press(card, "Dismiss");
        }

        Assert.Equal(3, _f.EditRequests.Count);
    }

    [Fact]
    public async Task Unsaved_changes_found_at_the_last_moment_offer_to_review_them()
    {
        await _f.PlayModdedAsync();
        _f.Backend.Launch.FailLaunch(FlowRig.Skyrim, ErrorInfo.From(new DirtyWorkingTreeException(RepoRole.External)));
        var card = _f.Inline<ErrorCardViewModel>();
        Assert.Equal(["Review the changes", "Dismiss"], card.Actions.Select(a => a.Label));
        var previews = _f.Backend.Calls.To<ILaunchApi>(nameof(ILaunchApi.GetPreviewAsync)).Count;

        FlowRig.Press(card, "Review the changes");

        Assert.Equal(previews + 1, _f.Backend.Calls.To<ILaunchApi>(nameof(ILaunchApi.GetPreviewAsync)).Count);
    }

    [Fact]
    public async Task Dismissing_a_failure_clears_the_card_and_says_so()
    {
        await _f.PlayModdedAsync();
        _f.Backend.Launch.FailLaunch(FlowRig.Skyrim, FlowRig.Spawn());
        var before = _f.InlineChanges.Count;

        FlowRig.Press(_f.Inline<ErrorCardViewModel>(), "Dismiss");

        Assert.Null(_f.Flow.InlineContentFor(FlowRig.Skyrim));
        Assert.True(_f.InlineChanges.Count > before);
    }

    [Fact]
    public async Task Im_done_playing_tells_the_service_and_the_exit_replaces_the_card_with_the_post_play_dialog()
    {
        await _f.PlayToRunningAsync();
        var card = _f.Inline<RunningCardViewModel>();

        await card.DonePlayingCommand.ExecuteAsync(null);
        Assert.Single(_f.Backend.Calls.To<ILaunchApi>(nameof(ILaunchApi.MarkFinishedAsync)));

        _f.Backend.Launch.ReportExited(FlowRig.Skyrim, MockupScenario.PlayDuration, MockupScenario.NewSaveFiles);

        Assert.Null(_f.Flow.InlineContentFor(FlowRig.Skyrim));
        Assert.Equal(0, _f.Rig.Clock.ActiveTimers);
        var dialog = _f.PostPlay;
        Assert.Equal("You played for 2 hours 14 minutes", dialog.Title);
        Assert.True(_f.Flow.IsActive(FlowRig.Skyrim));
    }

    [Fact]
    public async Task A_finish_the_service_could_not_take_is_reported()
    {
        await _f.PlayToRunningAsync();
        _f.Backend.FailNext<ILaunchApi>(nameof(ILaunchApi.MarkFinishedAsync), ErrorInfo.From(new NoActiveSessionException(FlowRig.Skyrim)));

        await _f.Inline<RunningCardViewModel>().DonePlayingCommand.ExecuteAsync(null);

        Assert.Single(_f.Rig.Toasts.Toasts);
        Assert.True(_f.Inline<RunningCardViewModel>().DonePlayingCommand.CanExecute(null));
    }

    [Fact]
    public async Task With_session_capture_off_nothing_is_asked_and_nothing_is_captured()
    {
        FakeAssert.Ok(await _f.Backend.Settings.SaveAsync(MockupScenario.Settings with { ConfirmSessionCapture = false }));

        await _f.PlayToExitedAsync();

        Assert.Empty(_f.Dialogs.Shown.OfType<PostPlayDialogViewModel>());
        var complete = Assert.Single(_f.Completes());
        Assert.Equal([FlowRig.Skyrim, new SessionCompletion.Skip()], complete.Args);
        Assert.Empty(_f.Backend.Calls.To<IProfilesApi>(nameof(IProfilesApi.CaptureAsync)));
        Assert.False(_f.Flow.IsActive(FlowRig.Skyrim));
        Assert.Null(_f.Flow.InlineContentFor(FlowRig.Skyrim));
    }

    [Fact]
    public async Task A_setting_that_cannot_be_read_means_the_player_is_asked()
    {
        FakeAssert.Ok(await _f.Backend.Settings.SaveAsync(MockupScenario.Settings with { ConfirmSessionCapture = false }));
        await _f.PlayToRunningAsync();
        _f.Backend.FailNext<ISettingsApi>(nameof(ISettingsApi.GetAsync), MockupScenario.FolderGoneError);
        _f.Backend.Launch.ReportExited(FlowRig.Skyrim, MockupScenario.PlayDuration, 3);

        Assert.Single(_f.Dialogs.Shown.OfType<PostPlayDialogViewModel>());
    }

    [Fact]
    public async Task Save_it_completes_the_session_and_shows_the_capture_until_it_ends()
    {
        await _f.PlayToExitedAsync();
        var dialog = _f.PostPlay;

        await dialog.SaveCommand.ExecuteAsync(null);

        var complete = Assert.Single(_f.Completes());
        Assert.Equal([FlowRig.Skyrim, new SessionCompletion.CaptureNew("Session - 7-8-26")], complete.Args);
        var saving = _f.Inline<LaunchSavingViewModel>();
        Assert.Equal("Saving your session…", saving.Title);
        Assert.True(saving.IsIndeterminate);
        Assert.True(_f.Flow.IsActive(FlowRig.Skyrim));

        _f.Backend.Jobs.ReportProgress(CaptureJob(), new JobProgress(FilesDone: 5, FilesTotal: 10));
        Assert.False(saving.IsIndeterminate);
        Assert.Equal("5 of 10 files", saving.ProgressText);
        Assert.Equal(50, saving.PercentValue);

        _f.Backend.Jobs.Complete(CaptureJob());

        Assert.Null(_f.Flow.InlineContentFor(FlowRig.Skyrim));
        Assert.False(_f.Flow.IsActive(FlowRig.Skyrim));
    }

    [Fact]
    public async Task Cancel_on_the_saving_card_cancels_the_capture_job()
    {
        await _f.PlayToExitedAsync();
        await _f.PostPlay.SaveCommand.ExecuteAsync(null);

        await _f.Inline<LaunchSavingViewModel>().CancelCommand.ExecuteAsync(null);

        var cancel = Assert.Single(_f.Backend.Calls.To<IJobsApi>(nameof(IJobsApi.CancelAsync)));
        Assert.Equal([CaptureJob()], cancel.Args);
    }

    [Fact]
    public async Task Not_now_completes_the_session_at_once_with_no_capture()
    {
        await _f.PlayToExitedAsync();

        _f.PostPlay.NotNowCommand.Execute(null);

        Assert.Equal([FlowRig.Skyrim, new SessionCompletion.Skip()], Assert.Single(_f.Completes()).Args);
        Assert.Null(_f.Flow.InlineContentFor(FlowRig.Skyrim));
        Assert.False(_f.Flow.IsActive(FlowRig.Skyrim));
        Assert.DoesNotContain(_f.Backend.Jobs.Started, job => job.Kind == JobKind.Capture);
    }

    [Fact]
    public async Task Escape_on_the_post_play_dialog_leaves_the_saves_alone()
    {
        await _f.PlayToExitedAsync();

        _f.PostPlay.RequestDismiss();

        Assert.Equal([FlowRig.Skyrim, new SessionCompletion.Skip()], Assert.Single(_f.Completes()).Args);
        Assert.False(_f.Flow.IsActive(FlowRig.Skyrim));
    }

    [Fact]
    public async Task A_capture_that_saved_only_one_folder_shows_the_half_of_that_save_card_never_a_success()
    {
        await _f.PlayToExitedAsync();
        await _f.PostPlay.SaveCommand.ExecuteAsync(null);

        _f.Backend.Jobs.Complete(CaptureJob(), MockupScenario.HalfSaved);

        var card = _f.Inline<ErrorCardViewModel>();
        Assert.Equal("Half of that save worked", card.Title);
        Assert.Equal(
            "Your game version and mods were saved. Your save files weren't. There's not enough free disk space to save this profile right now. Fix that and retry just that part; nothing you already saved is affected.",
            card.Body);
        var rows = Assert.IsType<FolderOutcomeListViewModel>(card.Supplement).Rows;
        Assert.Equal(["Game version and mods · saved", "Saves · not saved"], rows.Select(r => r.Text));
        Assert.Equal([true, false], rows.Select(r => r.IsOk));
        Assert.Equal(ErrorTone.Warning, card.Tone);
        Assert.Equal(["Retry the saves", "Dismiss"], card.Actions.Select(a => a.Label));
        Assert.True(FlowRig.Action(card, "Retry the saves").IsPrimary);
        Assert.False(_f.Flow.IsActive(FlowRig.Skyrim));
    }

    [Fact]
    public async Task Retrying_the_saves_asks_for_the_same_choice_again_and_never_starts_a_plain_capture()
    {
        await _f.PlayToExitedAsync();
        await _f.PostPlay.SaveCommand.ExecuteAsync(null);
        _f.Backend.Jobs.Complete(CaptureJob(), MockupScenario.HalfSaved);

        FlowRig.Press(_f.Inline<ErrorCardViewModel>(), "Retry the saves");

        Assert.Equal([FlowRig.Skyrim], Assert.Single(_f.Backend.Calls.To<ILaunchApi>(nameof(ILaunchApi.RetryCompletionAsync))).Args);
        Assert.Empty(_f.Backend.Calls.To<IProfilesApi>(nameof(IProfilesApi.CaptureAsync)));
        Assert.IsType<LaunchSavingViewModel>(_f.Flow.InlineContentFor(FlowRig.Skyrim));
        Assert.True(_f.Flow.IsActive(FlowRig.Skyrim));

        _f.Backend.Jobs.Complete(CaptureJob(), new CaptureResult(MockupScenario.ModdedId, MockupScenario.BeforeFinalBoss, [new FolderOutcome(RepoRole.External)]));

        Assert.Null(_f.Flow.InlineContentFor(FlowRig.Skyrim));
        Assert.False(_f.Flow.IsActive(FlowRig.Skyrim));
    }

    [Fact]
    public async Task A_retry_that_fails_again_brings_the_card_back()
    {
        await _f.PlayToExitedAsync();
        await _f.PostPlay.SaveCommand.ExecuteAsync(null);
        _f.Backend.Jobs.Complete(CaptureJob(), MockupScenario.HalfSaved);
        FlowRig.Press(_f.Inline<ErrorCardViewModel>(), "Retry the saves");

        _f.Backend.Jobs.Complete(CaptureJob(), new CaptureResult(MockupScenario.ModdedId, null, [new FolderOutcome(RepoRole.External, MockupScenario.SavesOutOfRoomError)]));

        Assert.Equal("There isn't enough room", _f.Inline<ErrorCardViewModel>().Title);
        Assert.False(_f.Flow.IsActive(FlowRig.Skyrim));
    }

    [Fact]
    public async Task A_retry_that_cannot_start_is_reported_and_the_card_stays()
    {
        await _f.PlayToExitedAsync();
        await _f.PostPlay.SaveCommand.ExecuteAsync(null);
        _f.Backend.Jobs.Complete(CaptureJob(), MockupScenario.HalfSaved);
        var card = _f.Inline<ErrorCardViewModel>();
        _f.Backend.FailNext<ILaunchApi>(nameof(ILaunchApi.RetryCompletionAsync), MockupScenario.SavesOutOfRoomError);

        FlowRig.Press(card, "Retry the saves");

        Assert.Same(card, _f.Flow.InlineContentFor(FlowRig.Skyrim));
        Assert.Single(_f.Rig.Toasts.Toasts);
        Assert.False(_f.Flow.IsActive(FlowRig.Skyrim));
    }

    [Fact]
    public async Task A_capture_that_failed_as_a_whole_shows_why_and_offers_to_try_again()
    {
        await _f.PlayToExitedAsync();
        await _f.PostPlay.SaveCommand.ExecuteAsync(null);

        _f.Backend.Jobs.Fail(CaptureJob(), MockupScenario.SavesOutOfRoomError);

        var card = _f.Inline<ErrorCardViewModel>();
        Assert.Equal("There isn't enough room", card.Title);
        Assert.Equal(["Try again", "Dismiss"], card.Actions.Select(a => a.Label));

        FlowRig.Press(card, "Try again");

        Assert.Equal([FlowRig.Skyrim], Assert.Single(_f.Backend.Calls.To<ILaunchApi>(nameof(ILaunchApi.RetryCompletionAsync))).Args);
        Assert.IsType<LaunchSavingViewModel>(_f.Flow.InlineContentFor(FlowRig.Skyrim));
    }

    [Fact]
    public async Task A_cancelled_capture_shows_a_canceled_card_and_the_changes_stay_on_disk()
    {
        await _f.PlayToExitedAsync();
        await _f.PostPlay.SaveCommand.ExecuteAsync(null);

        _f.Backend.Jobs.CompleteCancelled(CaptureJob());

        var card = _f.Inline<ErrorCardViewModel>();
        Assert.Equal("Canceled", card.Title);
        Assert.Equal(ErrorTone.Neutral, card.Tone);
        Assert.False(_f.Flow.IsActive(FlowRig.Skyrim));
    }

    [Fact]
    public async Task A_post_play_choice_the_service_refuses_shows_a_card()
    {
        await _f.PlayToExitedAsync();
        _f.Backend.FailNext<ILaunchApi>(nameof(ILaunchApi.CompleteSessionAsync), ErrorInfo.From(new NoActiveSessionException(FlowRig.Skyrim)));

        await _f.PostPlay.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Something went wrong", _f.Inline<ErrorCardViewModel>().Title);
        Assert.Contains("isn't tracking a play session", _f.Inline<ErrorCardViewModel>().Body);
    }

    [Fact]
    public async Task Playing_from_an_older_point_offers_a_new_profile_by_default_and_creates_it()
    {
        await _f.PlayWithProgressAsync();
        _f.Backend.Launch.ReportRunning(FlowRig.Skyrim, MockupScenario.RunningSince);
        _f.Backend.Launch.ReportExited(FlowRig.Skyrim, MockupScenario.PlayDuration, 2);
        var dialog = _f.PostPlay;
        Assert.Equal(CompletionChoice.ForkNewProfile, dialog.SelectedChoice.Kind);

        await dialog.SaveCommand.ExecuteAsync(null);

        Assert.Equal([FlowRig.Skyrim, new SessionCompletion.ForkNewProfile("Kept changes - 7-8-26", "Kept changes - 7-8-26")], Assert.Single(_f.Completes()).Args);
        _f.Backend.Jobs.Complete(CaptureJob());
        Assert.Null(_f.Flow.InlineContentFor(FlowRig.Skyrim));
    }

    [Fact]
    public async Task Updating_the_latest_save_asks_first_and_then_completes_with_an_update()
    {
        await _f.PlayToExitedAsync();
        var dialog = _f.PostPlay;
        dialog.SelectedChoice = dialog.Choices.Single(c => c.Kind == CompletionChoice.UpdateExisting);
        _f.Dialogs.AnswerConfirm(true);

        await dialog.SaveCommand.ExecuteAsync(null);

        Assert.Equal([FlowRig.Skyrim, new SessionCompletion.UpdateExisting("Before the final boss")], Assert.Single(_f.Completes()).Args);
    }

    [Fact]
    public async Task Any_kind_of_save_that_failed_as_a_whole_can_be_tried_again_except_when_the_folder_moved_on()
    {
        await _f.PlayToExitedAsync();
        var dialog = _f.PostPlay;
        dialog.SelectedChoice = dialog.Choices.Single(c => c.Kind == CompletionChoice.UpdateExisting);
        _f.Dialogs.AnswerConfirm(true);
        await dialog.SaveCommand.ExecuteAsync(null);

        _f.Backend.Jobs.Fail(CaptureJob(), MockupScenario.SavesOutOfRoomError);

        Assert.Equal(["Try again", "Dismiss"], _f.Inline<ErrorCardViewModel>().Actions.Select(a => a.Label));

        FlowRig.Press(_f.Inline<ErrorCardViewModel>(), "Try again");
        _f.Backend.Jobs.Fail(CaptureJob(), ErrorInfo.From(new SessionMovedException(RepoRole.External, "the folder is on another save")));

        Assert.Equal(["Dismiss"], _f.Inline<ErrorCardViewModel>().Actions.Select(a => a.Label));
    }

    [Fact]
    public async Task A_new_play_after_a_finished_session_starts_a_fresh_flow()
    {
        await _f.PlayToExitedAsync();
        _f.PostPlay.NotNowCommand.Execute(null);

        await _f.Play(LaunchRig.ModdedTip);

        Assert.Equal(2, _f.Starts().Count);
        Assert.IsType<LaunchBannerViewModel>(_f.Flow.InlineContentFor(FlowRig.Skyrim));
        Assert.True(_f.Flow.IsActive(FlowRig.Skyrim));
    }
}
