using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.Common;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.Launch;

// A Steam game started from its program quits when Steam is closed, and starting it through Steam could install an update over the version
// being loaded. So the flow asks first, in plain words, and only ever starts the Steam client itself (and only when the player agrees).
public class LaunchSteamFlowTests : IDisposable
{
    private readonly FlowRig _f = new();

    public void Dispose() => _f.Dispose();

    private static ErrorInfo Closed() => ErrorInfo.From(new SteamNotRunningException(FlowRig.Skyrim));

    private async Task SteamClosedAsync(LaunchTarget? target = null)
    {
        target ??= LaunchRig.ModdedTip;
        var source = FakeAssert.Ok(await _f.Backend.Launch.GetPreviewAsync(FlowRig.Skyrim, target, TestContext.Current.CancellationToken));
        _f.Backend.State.LaunchPreviews[(FlowRig.Skyrim, target)] = source with { SteamNeeded = Closed() };
    }

    // The Survival profile has unsaved saves, so a launch that got past the Steam question would open its dialog; the question is asked
    // before that, because a dialog can discard unsaved work and a player who says Not now must find everything as it was.
    [Fact]
    public async Task A_Steam_game_with_Steam_closed_asks_to_start_Steam_before_any_dialog_or_start()
    {
        await SteamClosedAsync(LaunchRig.SurvivalTip);
        Assert.Contains(FakeAssert.Ok(await _f.Backend.Launch.GetPreviewAsync(FlowRig.Skyrim, LaunchRig.SurvivalTip, TestContext.Current.CancellationToken)).Folders, folder => folder.Dirty);

        var begin = _f.Play(LaunchRig.SurvivalTip);
        var answered = await Task.WhenAny(begin, Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)) == begin;
        if (!answered)
        {
            // The flow went on to its dialog instead of asking about Steam; end it so the test fails on the assertion and not on a hang.
            _f.LaunchDialog.CancelCommand.Execute(null);
            await begin;
        }

        Assert.True(answered, "the launch opened a dialog before it asked about Steam");
        var card = _f.Inline<ErrorCardViewModel>();
        Assert.Equal("Steam needs to be running", card.Title);
        Assert.Equal(
            "This game needs Steam to be running before it can start. SavePoint can start Steam for you, then carry on. If this game doesn't need Steam, you can play without it. Nothing in your game folders has changed.",
            card.Body);
        Assert.Equal(["Start Steam", "Play without Steam", "Not now"], card.Actions.Select(action => action.Label));
        Assert.True(FlowRig.Action(card, "Start Steam").IsPrimary);
        Assert.Empty(_f.Dialogs.Shown);
        Assert.Empty(_f.Starts());
        Assert.False(_f.Flow.IsActive(FlowRig.Skyrim));
    }

    [Fact]
    public async Task Play_without_Steam_starts_the_game_asking_the_service_to_skip_the_Steam_check()
    {
        await SteamClosedAsync();
        await _f.Play(LaunchRig.ModdedTip);

        await FlowRig.AsyncCommand(FlowRig.Action(_f.Inline<ErrorCardViewModel>(), "Play without Steam")).ExecuteAsync(null);

        var start = Assert.Single(_f.Starts());
        Assert.Equal([FlowRig.Skyrim, LaunchRig.ModdedTip, false, true], start.Args);
        Assert.True(_f.Flow.IsActive(FlowRig.Skyrim));
    }

    [Fact]
    public async Task Play_without_Steam_on_the_card_after_a_refused_start_tries_again_with_the_same_choice()
    {
        _f.Backend.FailNext<ILaunchApi>(nameof(ILaunchApi.StartAsync), Closed());
        await _f.Play(LaunchRig.ModdedTip);

        await FlowRig.AsyncCommand(FlowRig.Action(_f.Inline<ErrorCardViewModel>(), "Play without Steam")).ExecuteAsync(null);

        Assert.Equal(2, _f.Starts().Count);
        Assert.Equal([FlowRig.Skyrim, LaunchRig.ModdedTip, false, true], _f.Starts()[1].Args);
    }

    [Fact]
    public async Task Play_without_Steam_is_asked_for_once_so_the_next_launch_asks_again()
    {
        await SteamClosedAsync();
        await _f.Play(LaunchRig.ModdedTip);
        await FlowRig.AsyncCommand(FlowRig.Action(_f.Inline<ErrorCardViewModel>(), "Play without Steam")).ExecuteAsync(null);
        _f.Backend.Launch.ReportRunning(FlowRig.Skyrim, SampleScenario.RunningSince);
        _f.Backend.Launch.ReportExited(FlowRig.Skyrim, SampleScenario.PlayDuration, 0);
        await _f.Backend.Launch.CompleteSessionAsync(FlowRig.Skyrim, new SessionCompletion.Skip());

        await _f.Play(LaunchRig.ModdedTip);

        Assert.Equal("Steam needs to be running", _f.Inline<ErrorCardViewModel>().Title);
        Assert.Single(_f.Starts());
    }

    [Fact]
    public async Task Not_now_puts_the_card_away_and_starts_nothing()
    {
        await SteamClosedAsync(LaunchRig.SurvivalTip);
        await _f.Play(LaunchRig.SurvivalTip);

        FlowRig.Press(_f.Inline<ErrorCardViewModel>(), "Not now");

        Assert.Null(_f.Flow.InlineContentFor(FlowRig.Skyrim));
        Assert.Empty(_f.Starts());
        Assert.Empty(_f.Dialogs.Shown);
        Assert.False(_f.Flow.IsActive(FlowRig.Skyrim));
    }

    [Fact]
    public async Task Start_Steam_goes_on_with_the_launch_and_tells_the_service_it_may_start_the_client()
    {
        await SteamClosedAsync();
        await _f.Play(LaunchRig.ModdedTip);

        await FlowRig.AsyncCommand(FlowRig.Action(_f.Inline<ErrorCardViewModel>(), "Start Steam")).ExecuteAsync(null);

        var start = Assert.Single(_f.Starts());
        Assert.Equal([FlowRig.Skyrim, LaunchRig.ModdedTip, true], start.Args);
        Assert.True(_f.Flow.IsActive(FlowRig.Skyrim));
    }

    [Fact]
    public async Task A_plain_launch_never_passes_the_start_Steam_flag()
    {
        await _f.Play(LaunchRig.ModdedTip);

        Assert.Equal([FlowRig.Skyrim, LaunchRig.ModdedTip], Assert.Single(_f.Starts()).Args);
    }

    [Fact]
    public async Task Steam_closing_between_the_check_and_the_start_shows_the_same_card_and_Start_Steam_tries_again_with_the_flag()
    {
        _f.Backend.FailNext<ILaunchApi>(nameof(ILaunchApi.StartAsync), Closed());
        await _f.Play(LaunchRig.ModdedTip);

        var card = _f.Inline<ErrorCardViewModel>();
        Assert.Equal("Steam needs to be running", card.Title);
        Assert.Equal(["Start Steam", "Play without Steam", "Not now"], card.Actions.Select(action => action.Label));
        Assert.False(_f.Flow.IsActive(FlowRig.Skyrim));

        await FlowRig.AsyncCommand(FlowRig.Action(card, "Start Steam")).ExecuteAsync(null);

        Assert.Equal(2, _f.Starts().Count);
        Assert.Equal([FlowRig.Skyrim, LaunchRig.ModdedTip, true], _f.Starts()[1].Args);
    }

    [Fact]
    public async Task The_flag_is_used_once_so_the_next_launch_asks_again()
    {
        await SteamClosedAsync();
        await _f.Play(LaunchRig.ModdedTip);
        await FlowRig.AsyncCommand(FlowRig.Action(_f.Inline<ErrorCardViewModel>(), "Start Steam")).ExecuteAsync(null);
        _f.Backend.Launch.ReportRunning(FlowRig.Skyrim, SampleScenario.RunningSince);
        _f.Backend.Launch.ReportExited(FlowRig.Skyrim, SampleScenario.PlayDuration, 0);
        await _f.Backend.Launch.CompleteSessionAsync(FlowRig.Skyrim, new SessionCompletion.Skip());

        await _f.Play(LaunchRig.ModdedTip);

        Assert.Equal("Steam needs to be running", _f.Inline<ErrorCardViewModel>().Title);
        Assert.Single(_f.Starts());
    }

    [Fact]
    public async Task Loading_the_files_without_playing_never_asks_about_Steam()
    {
        await SteamClosedAsync();

        await _f.Flow.BeginSwitchAsync(FlowRig.Skyrim, LaunchRig.ModdedTip);

        var card = _f.Inline<ErrorCardViewModel>();
        Assert.DoesNotContain(card.Actions, action => action.Label == "Start Steam");
        Assert.NotEqual("Steam needs to be running", card.Title);
    }

    [Fact]
    public async Task While_the_service_waits_for_Steam_the_flow_says_so_and_Stop_waiting_cancels_the_launch()
    {
        await _f.PlayModdedAsync();
        var session = _f.Session();

        _f.Backend.Jobs.ReportStep(session.LaunchJobId, JobSteps.LaunchWaitingForSteam, ("game", "Skyrim Special Edition"));

        var card = _f.Inline<ErrorCardViewModel>();
        Assert.Equal("Waiting for Steam to start…", card.Title);
        Assert.Equal(
            "SavePoint started Steam for Skyrim Special Edition. When Steam has signed in, the game starts. Nothing in your game folders has changed yet.",
            card.Body);
        Assert.Equal(["Stop waiting"], card.Actions.Select(action => action.Label));
        Assert.True(_f.Flow.IsActive(FlowRig.Skyrim));

        await FlowRig.AsyncCommand(FlowRig.Action(card, "Stop waiting")).ExecuteAsync(null);

        var cancel = Assert.Single(_f.Backend.Calls.To<IJobsApi>(nameof(IJobsApi.CancelAsync)));
        Assert.Equal(session.LaunchJobId, cancel.Args[0]);
    }

    [Fact]
    public async Task The_waiting_card_gives_way_to_the_switching_dialog_once_Steam_is_up()
    {
        await _f.PlayModdedAsync();
        _f.Backend.Jobs.ReportStep(_f.Session().LaunchJobId, JobSteps.LaunchWaitingForSteam, ("game", "Skyrim Special Edition"));

        _f.Backend.Launch.ReportSwitching(FlowRig.Skyrim, SampleScenario.SwitchProgress);

        Assert.Null(_f.Flow.InlineContentFor(FlowRig.Skyrim));
        Assert.False(_f.Progress.IsClosed);
    }

    [Fact]
    public async Task The_waiting_card_ends_once_Steam_is_signed_in_and_the_banner_comes_back_while_the_folders_are_read()
    {
        await _f.PlayModdedAsync();
        var job = _f.Session().LaunchJobId;
        _f.Backend.Jobs.ReportStep(job, JobSteps.LaunchWaitingForSteam, ("game", "Skyrim Special Edition"));
        Assert.Equal("Waiting for Steam to start…", _f.Inline<ErrorCardViewModel>().Title);

        _f.Backend.Jobs.ReportStep(job, JobSteps.LaunchSteamReady, ("game", "Skyrim Special Edition"));

        Assert.IsType<Savepoint.App.Features.Launch.LaunchBannerViewModel>(_f.Flow.InlineContentFor(FlowRig.Skyrim));
    }

    [Fact]
    public async Task The_waiting_card_ends_once_Steam_is_signed_in_and_the_progress_dialog_comes_back_for_the_folder_read()
    {
        await _f.PlayWithProgressAsync();
        var job = _f.Session().LaunchJobId;
        _f.Backend.Jobs.ReportStep(job, JobSteps.LaunchWaitingForSteam, ("game", "Skyrim Special Edition"));
        var shownBefore = _f.Dialogs.Shown.OfType<Savepoint.App.Features.Launch.SwitchProgressDialogViewModel>().Count();

        _f.Backend.Jobs.ReportStep(job, JobSteps.LaunchSteamReady, ("game", "Skyrim Special Edition"));

        Assert.Null(_f.Flow.InlineContentFor(FlowRig.Skyrim));
        Assert.Equal(shownBefore + 1, _f.Dialogs.Shown.OfType<Savepoint.App.Features.Launch.SwitchProgressDialogViewModel>().Count());
        Assert.False(_f.Progress.IsClosed);
    }

    [Fact]
    public async Task The_waiting_card_does_not_come_back_for_a_later_change_to_the_same_job()
    {
        await _f.PlayModdedAsync();
        var job = _f.Session().LaunchJobId;
        _f.Backend.Jobs.ReportStep(job, JobSteps.LaunchWaitingForSteam, ("game", "Skyrim Special Edition"));
        _f.Backend.Jobs.ReportStep(job, JobSteps.LaunchSteamReady, ("game", "Skyrim Special Edition"));

        _f.Backend.Jobs.ReportStep(job, JobSteps.LaunchFolderInPlace, ("folder", RepoRole.Install), ("profile", "Modded"));

        Assert.IsType<Savepoint.App.Features.Launch.LaunchBannerViewModel>(_f.Flow.InlineContentFor(FlowRig.Skyrim));
    }

    // The service waits a few seconds after the game quits to be sure a store did not start it again; the screen says so meanwhile.
    [Fact]
    public async Task A_save_waits_with_a_card_that_says_the_game_is_being_checked_and_the_card_is_gone_when_the_answer_comes()
    {
        await _f.PlayToExitedAsync();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _f.Backend.Launch.CompleteGate = () => release.Task;

        await _f.PostPlay.SaveCommand.ExecuteAsync(null);

        var card = _f.Inline<ErrorCardViewModel>();
        Assert.Equal("Checking the game has closed…", card.Title);
        Assert.Empty(card.Actions);

        _f.Backend.Launch.CompleteGate = null;
        release.SetResult();
        Assert.True(await UiWait.UntilAsync(() => _f.Flow.InlineContentFor(FlowRig.Skyrim) is not ErrorCardViewModel { Title: "Checking the game has closed…" }), "the card stayed after the save was answered");
    }

    [Fact]
    public async Task Skipping_the_save_shows_no_card_about_checking_the_game()
    {
        await _f.PlayToExitedAsync();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _f.Backend.Launch.CompleteGate = () => release.Task;

        _f.PostPlay.ChooseNotNow();

        Assert.Null(_f.Flow.InlineContentFor(FlowRig.Skyrim));
        release.SetResult();
    }

    [Fact]
    public async Task The_waiting_card_gives_way_to_the_running_card_when_the_game_starts()
    {
        await _f.PlayModdedAsync();
        _f.Backend.Jobs.ReportStep(_f.Session().LaunchJobId, JobSteps.LaunchWaitingForSteam, ("game", "Skyrim Special Edition"));

        _f.Backend.Launch.ReportRunning(FlowRig.Skyrim, SampleScenario.RunningSince);

        Assert.IsType<Savepoint.App.Features.Launch.RunningCardViewModel>(_f.Flow.InlineContentFor(FlowRig.Skyrim));
    }

    // The service refuses a save while the game is still open; the session stays over, and the player must be able to choose again once it closes.
    [Fact]
    public async Task A_save_refused_because_the_game_is_still_open_offers_to_choose_again()
    {
        await _f.PlayToExitedAsync();
        _f.Backend.FailNext<ILaunchApi>(nameof(ILaunchApi.CompleteSessionAsync), ErrorInfo.From(new SessionStillRunningException(FlowRig.Skyrim)));
        await _f.PostPlay.SaveCommand.ExecuteAsync(null);

        var card = _f.Inline<ErrorCardViewModel>();
        Assert.Equal("The game is still running. Close it, then try again.", card.Body);
        Assert.Equal(["Choose again", "Dismiss"], card.Actions.Select(action => action.Label));
        Assert.True(FlowRig.Action(card, "Choose again").IsPrimary);
        var dialogsBefore = _f.Dialogs.Shown.OfType<Savepoint.App.Features.Launch.PostPlayDialogViewModel>().Count();

        FlowRig.Press(card, "Choose again");

        Assert.Equal(dialogsBefore + 1, _f.Dialogs.Shown.OfType<Savepoint.App.Features.Launch.PostPlayDialogViewModel>().Count());
        Assert.Null(_f.Flow.InlineContentFor(FlowRig.Skyrim));
    }

    [Fact]
    public async Task A_Steam_that_did_not_come_up_says_so_and_offers_try_again()
    {
        await _f.PlayModdedAsync();

        _f.Backend.Launch.FailLaunch(FlowRig.Skyrim, ErrorInfo.From(new SteamDidNotStartException(FlowRig.Skyrim)));

        var card = _f.Inline<ErrorCardViewModel>();
        Assert.Equal("Steam isn't ready", card.Title);
        Assert.Equal(
            "Steam didn't finish starting. If it asks you to sign in, sign in, then press Play again. Nothing in your game folders has changed.",
            card.Body);
        Assert.Equal(["Try again", "Dismiss"], card.Actions.Select(action => action.Label));
        Assert.False(_f.Flow.IsActive(FlowRig.Skyrim));
    }
}
