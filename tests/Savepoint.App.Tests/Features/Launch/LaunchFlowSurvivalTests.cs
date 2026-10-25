using Avalonia.Headless.XUnit;
using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Features.Launch;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.Shell;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Launch;

// D13.19: the flow's state lives above every screen, so changing screens, layouts or the selected game never loses a session or its prompt.
public class LaunchFlowSurvivalTests
{
    [Fact]
    public async Task A_running_game_keeps_its_card_through_navigation_layout_changes_and_selecting_other_games()
    {
        using var f = new FlowRig();
        var shell = (await ShellRig.StartAsync(f.Backend, launchFlow: f.Flow)).Shell;
        await f.PlayToRunningAsync();
        var card = f.Inline<RunningCardViewModel>();

        shell.Navigate(NavDestination.Settings);
        shell.Navigate(NavDestination.History);
        await shell.SetLayoutAsync(ShellLayout.Library);
        shell.SelectGame(SampleScenario.SatisfactoryId);
        shell.Navigate(NavDestination.Games);
        await shell.SetLayoutAsync(ShellLayout.Sidebar);

        Assert.Same(card, f.Flow.InlineContentFor(SampleScenario.SkyrimId));
        Assert.True(f.Flow.IsActive(SampleScenario.SkyrimId));
        Assert.Null(f.Flow.InlineContentFor(SampleScenario.SatisfactoryId));
        Assert.False(f.Flow.IsActive(SampleScenario.SatisfactoryId));

        shell.SelectGame(SampleScenario.SkyrimId);
        Assert.Same(card, f.Flow.InlineContentFor(SampleScenario.SkyrimId));
    }

    [Fact]
    public async Task The_running_card_keeps_counting_while_the_user_is_elsewhere()
    {
        using var f = new FlowRig();
        var shell = (await ShellRig.StartAsync(f.Backend, launchFlow: f.Flow)).Shell;
        await f.PlayToRunningAsync();
        var card = f.Inline<RunningCardViewModel>();

        shell.Navigate(NavDestination.Settings);
        f.Rig.Clock.Advance(TimeSpan.FromMinutes(2));
        shell.Navigate(NavDestination.Games);

        Assert.EndsWith("started 43 minutes ago", card.Subtitle);
    }

    [Fact]
    public async Task The_post_play_prompt_waits_for_an_answer_however_the_user_moves_around()
    {
        using var f = new FlowRig();
        var shell = (await ShellRig.StartAsync(f.Backend, launchFlow: f.Flow)).Shell;
        await f.PlayToExitedAsync();
        var dialog = f.PostPlay;

        shell.Navigate(NavDestination.Settings);
        await shell.SetLayoutAsync(ShellLayout.Library);
        shell.SelectGame(SampleScenario.CaretakerId);
        shell.Navigate(NavDestination.Games);

        Assert.False(dialog.IsClosed);
        Assert.True(f.Flow.IsActive(SampleScenario.SkyrimId));
        Assert.Same(dialog, f.PostPlay);

        await dialog.SaveCommand.ExecuteAsync(null);
        Assert.Equal("Taking a snapshot of your session…", f.Inline<LaunchSavingViewModel>().Title);
    }

    [Fact]
    public async Task A_running_session_found_when_the_flow_is_built_shows_its_card()
    {
        using var rig = new LaunchRig();
        FakeAssert.Ok(await rig.Backend.Launch.StartAsync(SampleScenario.SkyrimId, LaunchRig.ModdedTip));
        rig.Backend.Launch.ReportRunning(SampleScenario.SkyrimId, SampleScenario.RunningSince);

        using var flow = new LaunchFlowViewModel(rig.Deps);

        var card = Assert.IsType<RunningCardViewModel>(flow.InlineContentFor(SampleScenario.SkyrimId));
        Assert.Equal("Skyrim Special Edition is running", card.Title);
        Assert.Equal("Modded playthrough - 7-2-26 · started 41 minutes ago", card.Subtitle);
        Assert.True(flow.IsActive(SampleScenario.SkyrimId));
    }

    [Fact]
    public async Task A_session_that_already_exited_when_the_flow_is_built_asks_what_to_do()
    {
        using var rig = new LaunchRig();
        await rig.ExitedAsync();

        using var flow = new LaunchFlowViewModel(rig.Deps);

        Assert.Single(rig.Dialogs.Shown.OfType<PostPlayDialogViewModel>());
        Assert.True(flow.IsActive(SampleScenario.SkyrimId));
    }

    [Fact]
    public async Task Sessions_that_already_ended_are_not_shown_again()
    {
        using var rig = new LaunchRig();
        FakeAssert.Ok(await rig.Backend.Launch.StartAsync(SampleScenario.SkyrimId, LaunchRig.ModdedTip));
        rig.Backend.Launch.FailLaunch(SampleScenario.SkyrimId, SampleScenario.FileInUseError);
        FakeAssert.Ok(await rig.Backend.Launch.StartAsync(SampleScenario.SatisfactoryId, new LaunchTarget.ProfileTip(SampleScenario.MainFactoryId)));
        rig.Backend.Launch.ReportRunning(SampleScenario.SatisfactoryId, SampleScenario.RunningSince);
        rig.Backend.Launch.ReportExited(SampleScenario.SatisfactoryId, TimeSpan.FromMinutes(5), 0);
        FakeAssert.Ok(await rig.Backend.Launch.CompleteSessionAsync(SampleScenario.SatisfactoryId, new SessionCompletion.Skip()));

        using var flow = new LaunchFlowViewModel(rig.Deps);

        Assert.Null(flow.InlineContentFor(SampleScenario.SkyrimId));
        Assert.Null(flow.InlineContentFor(SampleScenario.SatisfactoryId));
        Assert.Empty(flow.ActiveGameIds);
        Assert.Empty(rig.Dialogs.Shown);
    }

    [Fact]
    public async Task A_session_found_when_the_flow_is_built_can_be_tried_again_after_it_is_canceled()
    {
        using var rig = new LaunchRig();
        var started = FakeAssert.Ok(await rig.Backend.Launch.StartAsync(SampleScenario.SkyrimId, LaunchRig.ModdedTip));
        rig.Backend.Launch.ReportSwitching(SampleScenario.SkyrimId, SampleScenario.SwitchProgress);
        using var flow = new LaunchFlowViewModel(rig.Deps);
        Assert.Single(rig.Dialogs.Shown.OfType<SwitchProgressDialogViewModel>());
        var previews = rig.Backend.Calls.To<ILaunchApi>(nameof(ILaunchApi.GetPreviewAsync)).Count;

        rig.Backend.Jobs.CompleteCancelled(started.JobId);
        var card = Assert.IsType<ErrorCardViewModel>(flow.InlineContentFor(SampleScenario.SkyrimId));
        FlowRig.Press(card, "Try again");

        Assert.Equal(previews + 1, rig.Backend.Calls.To<ILaunchApi>(nameof(ILaunchApi.GetPreviewAsync)).Count);
        Assert.Equal(2, rig.Backend.Calls.To<ILaunchApi>(nameof(ILaunchApi.StartAsync)).Count);
    }

    [AvaloniaFact]
    public async Task The_app_composition_builds_the_real_flow_for_the_shell()
    {
        using var harness = await ShellViewHarness.StartAsync();

        Assert.IsType<LaunchFlowViewModel>(harness.Shell.LaunchFlow);
    }
}
