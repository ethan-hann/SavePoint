using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.Launch;

// A Steam game started from its program quits when Steam is closed, and starting it through Steam could install an update over the version
// being loaded. So the flow asks first, in plain words, and only ever starts the Steam client itself (and only when the player agrees).
public class LaunchSteamFlowTests : IDisposable
{
    private readonly FlowRig _f = new();

    public void Dispose() => _f.Dispose();

    private static ErrorInfo Closed() => ErrorInfo.From(new SteamNotRunningException(FlowRig.Skyrim));

    private async Task SteamClosedAsync()
    {
        var source = FakeAssert.Ok(await _f.Backend.Launch.GetPreviewAsync(FlowRig.Skyrim, LaunchRig.ModdedTip, TestContext.Current.CancellationToken));
        _f.Backend.State.LaunchPreviews[(FlowRig.Skyrim, LaunchRig.ModdedTip)] = source with { SteamNeeded = Closed() };
    }

    [Fact]
    public async Task A_Steam_game_with_Steam_closed_asks_to_start_Steam_before_any_dialog_or_start()
    {
        await SteamClosedAsync();

        await _f.Play(LaunchRig.ModdedTip);

        var card = _f.Inline<ErrorCardViewModel>();
        Assert.Equal("Steam needs to be running", card.Title);
        Assert.Equal(
            "This game needs Steam to be running before it can start. SavePoint can start Steam for you, then carry on. Nothing in your game folders has changed.",
            card.Body);
        Assert.Equal(["Start Steam", "Not now"], card.Actions.Select(action => action.Label));
        Assert.True(FlowRig.Action(card, "Start Steam").IsPrimary);
        Assert.Empty(_f.Dialogs.Shown);
        Assert.Empty(_f.Starts());
        Assert.False(_f.Flow.IsActive(FlowRig.Skyrim));
    }

    [Fact]
    public async Task Not_now_puts_the_card_away_and_starts_nothing()
    {
        await SteamClosedAsync();
        await _f.Play(LaunchRig.ModdedTip);

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
        Assert.Equal(["Start Steam", "Not now"], card.Actions.Select(action => action.Label));
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
    public async Task The_waiting_card_gives_way_to_the_running_card_when_the_game_starts()
    {
        await _f.PlayModdedAsync();
        _f.Backend.Jobs.ReportStep(_f.Session().LaunchJobId, JobSteps.LaunchWaitingForSteam, ("game", "Skyrim Special Edition"));

        _f.Backend.Launch.ReportRunning(FlowRig.Skyrim, SampleScenario.RunningSince);

        Assert.IsType<Savepoint.App.Features.Launch.RunningCardViewModel>(_f.Flow.InlineContentFor(FlowRig.Skyrim));
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
