using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Features.Editor;
using Savepoint.App.Features.Games;
using Savepoint.App.Features.Launch;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.Games;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.EndToEnd;

// Play on a profile through the real shell, Games, History, Settings and editor screens and the shell's launch flow,
// over the fake backend: what a person sees at each step, and that every screen agrees about it.
public class LaunchEndToEndTests
{
    private const string _skyrim = MockupScenario.SkyrimId;

    // History hands out its buttons as plain commands; the ones that start the flow are async ones.
    private static Task Run(System.Windows.Input.ICommand command)
    {
        var asynchronous = Assert.IsAssignableFrom<CommunityToolkit.Mvvm.Input.IAsyncRelayCommand>(command);
        return asynchronous.ExecuteAsync(null);
    }

    private static ProfileRowViewModel Row(ComposedApp app, string profileId) => app.Games.Profiles.Single(row => row.Id == profileId);

    private static void AssertPlayIs(ComposedApp app, bool enabled)
    {
        Assert.NotEmpty(app.Games.Profiles);
        Assert.All(app.Games.Profiles, row => Assert.Equal(enabled, row.CanPlay));
        Assert.All(app.Games.Profiles, row => Assert.Equal(enabled, row.PlayCommand.CanExecute(null)));
    }

    // History's buttons start the same flow, so they follow the same rule.
    private static void AssertHistoryButtonsAre(ComposedApp app, bool enabled)
    {
        var cards = app.History.Groups.SelectMany(group => group.Cards).Where(card => card.CanGoBack).ToList();
        Assert.NotEmpty(cards);
        Assert.All(cards, card => Assert.Equal(enabled, card.GoBackCommand.CanExecute(null)));
        Assert.All(app.History.Saves.Where(row => row.CanLoad), row => Assert.Equal(enabled, row.LoadCommand.CanExecute(null)));
    }

    [Fact]
    public async Task Half_a_save_after_play_is_reported_by_its_card_alone_with_no_toast_on_top_of_it()
    {
        using var app = await ComposedApp.StartAsync();
        await Row(app, MockupScenario.ModdedId).PlayCommand.ExecuteAsync(null).Soon();
        app.Backend.Launch.ReportRunning(_skyrim, MockupScenario.RunningSince);
        app.Backend.Launch.ReportExited(_skyrim, MockupScenario.PlayDuration, MockupScenario.NewSaveFiles);
        await app.Dialog<PostPlayDialogViewModel>().SaveCommand.ExecuteAsync(null).Soon();

        app.Backend.Jobs.Complete(app.Backend.Jobs.LastStarted(JobKind.Capture).JobId, MockupScenario.HalfSaved);

        Assert.IsType<ErrorCardViewModel>(app.Games.InlineContent);
        Assert.Empty(app.ToastTexts);
    }

    [Fact]
    public async Task A_profile_that_needs_a_switch_goes_from_the_dialog_through_the_session_to_a_saved_point_and_a_toast()
    {
        using var app = await ComposedApp.StartAsync();
        AssertPlayIs(app, true);

        var play = Row(app, MockupScenario.VanillaId).PlayCommand.ExecuteAsync(null);
        var dialog = app.Dialog<LaunchDialogViewModel>();

        Assert.Equal("Switch to \"Vanilla - latest update\"?", dialog.Title);
        Assert.True(dialog.CanSwitch);
        AssertPlayIs(app, false);

        dialog.SwitchCommand.Execute(null);
        await play.Soon();

        var start = Assert.Single(app.Backend.Calls.To<ILaunchApi>(nameof(ILaunchApi.StartAsync)));
        Assert.Equal([_skyrim, new LaunchTarget.ProfileTip(MockupScenario.VanillaId)], start.Args);
        var progress = app.Dialog<SwitchProgressDialogViewModel>();
        Assert.Equal("Getting Skyrim Special Edition ready…", progress.Title);
        Assert.Null(app.Games.InlineContent);

        app.Backend.Launch.ReportSwitching(_skyrim, MockupScenario.SwitchProgress);

        Assert.Equal("62%", progress.PercentText);
        Assert.False(progress.IsClosed);

        app.Backend.Launch.ReportRunning(_skyrim, MockupScenario.RunningSince);

        Assert.True(progress.IsClosed);
        var running = Assert.IsType<RunningCardViewModel>(app.Games.InlineContent);
        Assert.Equal("Skyrim Special Edition is running", running.Title);
        Assert.Equal("Vanilla - latest update · started 41 minutes ago", running.Subtitle);
        AssertPlayIs(app, false);

        app.Backend.Launch.ReportExited(_skyrim, MockupScenario.PlayDuration, MockupScenario.NewSaveFiles);

        var postPlay = app.Dialog<PostPlayDialogViewModel>();
        Assert.Equal("You played for 2 hours 14 minutes", postPlay.Title);
        Assert.Null(app.Games.InlineContent);
        Assert.Equal("Save it", postPlay.PrimaryLabel);

        await postPlay.SaveCommand.ExecuteAsync(null).Soon();

        var completion = Assert.Single(app.Backend.Calls.To<ILaunchApi>(nameof(ILaunchApi.CompleteSessionAsync)));
        Assert.IsType<SessionCompletion.CaptureNew>(completion.Args[1]);
        Assert.IsType<LaunchSavingViewModel>(app.Games.InlineContent);
        AssertPlayIs(app, false);
        Assert.Empty(app.ToastTexts);

        app.Backend.Jobs.Complete(app.Backend.Jobs.LastStarted(JobKind.Capture).JobId);

        Assert.Equal(["Saved Skyrim Special Edition."], app.ToastTexts);
        Assert.Null(app.Games.InlineContent);
        AssertPlayIs(app, true);
    }

    [Fact]
    public async Task A_profile_already_in_place_starts_at_once_under_the_banner_with_no_dialog()
    {
        using var app = await ComposedApp.StartAsync();

        await Row(app, MockupScenario.ModdedId).PlayCommand.ExecuteAsync(null).Soon();

        Assert.Empty(app.Dialogs.Shown.OfType<LaunchDialogViewModel>());
        var banner = Assert.IsType<LaunchBannerViewModel>(app.Games.InlineContent);
        Assert.Equal("Everything was already in place — starting Skyrim Special Edition now", banner.Title);
        Assert.Equal("Modded playthrough - 7-2-26 · save \"Before the final boss\" loaded", banner.Detail);
        var start = Assert.Single(app.Backend.Calls.To<ILaunchApi>(nameof(ILaunchApi.StartAsync)));
        Assert.Equal([_skyrim, new LaunchTarget.ProfileTip(MockupScenario.ModdedId)], start.Args);
        AssertPlayIs(app, false);

        app.Backend.Launch.ReportRunning(_skyrim, MockupScenario.RunningSince);

        Assert.IsType<RunningCardViewModel>(app.Games.InlineContent);
        Assert.Empty(app.Dialogs.Shown.OfType<SwitchProgressDialogViewModel>());
    }

    [Fact]
    public async Task Play_is_off_for_every_row_and_every_history_button_while_a_session_is_active_and_back_when_it_ends()
    {
        using var app = await ComposedApp.StartAsync();
        AssertPlayIs(app, true);
        AssertHistoryButtonsAre(app, true);

        await Row(app, MockupScenario.ModdedId).PlayCommand.ExecuteAsync(null).Soon();

        AssertPlayIs(app, false);
        AssertHistoryButtonsAre(app, false);

        app.Backend.Launch.ReportRunning(_skyrim, MockupScenario.RunningSince);
        app.Backend.Launch.ReportExited(_skyrim, MockupScenario.PlayDuration, MockupScenario.NewSaveFiles);
        AssertPlayIs(app, false);

        app.Dialog<PostPlayDialogViewModel>().NotNowCommand.Execute(null);

        Assert.Null(app.Games.InlineContent);
        AssertPlayIs(app, true);
        AssertHistoryButtonsAre(app, true);
    }

    [Fact]
    public async Task Go_back_to_this_on_a_history_point_runs_the_flow_for_that_point_and_ends_on_the_games_screen()
    {
        using var app = await ComposedApp.StartAsync();
        app.Shell.Navigate(NavDestination.History);
        var card = app.History.Groups.SelectMany(group => group.Cards).Single(c => c.Name == "Cleared Blackreach");

        var goBack = Run(card.GoBackCommand);
        var dialog = app.Dialog<LaunchDialogViewModel>();

        var preview = Assert.Single(app.Backend.Calls.To<ILaunchApi>(nameof(ILaunchApi.GetPreviewAsync)));
        Assert.Equal([_skyrim, new LaunchTarget.Point(MockupScenario.ClearedBlackreach)], preview.Args);
        Assert.True(dialog.IsOlderPoint);
        Assert.Equal(NavDestination.History, app.Shell.Destination);

        dialog.SwitchCommand.Execute(null);
        await goBack.Soon();

        var start = Assert.Single(app.Backend.Calls.To<ILaunchApi>(nameof(ILaunchApi.StartAsync)));
        Assert.Equal([_skyrim, new LaunchTarget.Point(MockupScenario.ClearedBlackreach)], start.Args);
        Assert.Equal(NavDestination.Games, app.Shell.Destination);
        Assert.NotEmpty(app.Dialogs.Shown.OfType<SwitchProgressDialogViewModel>());
        AssertPlayIs(app, false);

        app.Backend.Launch.ReportRunning(_skyrim, MockupScenario.RunningSince);

        Assert.IsType<RunningCardViewModel>(app.Games.InlineContent);
    }

    [Fact]
    public async Task Backing_out_of_the_dialog_started_from_history_leaves_history_showing_and_starts_nothing()
    {
        using var app = await ComposedApp.StartAsync();
        app.Shell.Navigate(NavDestination.History);

        var goBack = Run(app.History.Groups.SelectMany(group => group.Cards).Single(c => c.Name == "Cleared Blackreach").GoBackCommand);
        app.Dialog<LaunchDialogViewModel>().CancelCommand.Execute(null);
        await goBack.Soon();

        Assert.Equal(NavDestination.History, app.Shell.Destination);
        Assert.Empty(app.Backend.Calls.To<ILaunchApi>(nameof(ILaunchApi.StartAsync)));
        AssertPlayIs(app, true);
    }

    [Fact]
    public async Task A_missing_folder_shows_its_card_and_choosing_the_folder_opens_the_editor_for_that_game()
    {
        using var app = await ComposedApp.StartAsync();
        app.Backend.FailNext<ILaunchApi>(nameof(ILaunchApi.GetPreviewAsync), MockupScenario.FolderGoneError);

        await Row(app, MockupScenario.ModdedId).PlayCommand.ExecuteAsync(null).Soon();

        var card = Assert.IsType<ErrorCardViewModel>(app.Games.InlineContent);
        Assert.Equal("This game's folder has moved or is gone", card.Title);
        Assert.Equal(["Choose the folder", "Dismiss"], card.Actions.Select(action => action.Label));
        AssertPlayIs(app, true);

        card.Actions.Single(action => action.Label == "Choose the folder").Command.Execute(null);

        var editor = Assert.IsType<GameEditorViewModel>(app.Shell.Content);
        Assert.Equal("Edit Skyrim Special Edition", editor.Title);
        Assert.Equal(NavDestination.Games, app.Shell.Destination);
    }

    [Fact]
    public async Task A_missing_folder_met_from_history_takes_the_player_to_the_card_and_on_to_the_editor()
    {
        using var app = await ComposedApp.StartAsync();
        app.Shell.Navigate(NavDestination.History);
        app.Backend.FailNext<ILaunchApi>(nameof(ILaunchApi.GetPreviewAsync), MockupScenario.FolderGoneError);

        await Run(app.History.Saves.Single(row => row.Name == "Deep cave run").LoadCommand).Soon();

        Assert.Equal(NavDestination.Games, app.Shell.Destination);
        var card = Assert.IsType<ErrorCardViewModel>(app.Games.InlineContent);

        card.Actions.Single(action => action.Label == "Choose the folder").Command.Execute(null);

        Assert.Equal("Edit Skyrim Special Edition", Assert.IsType<GameEditorViewModel>(app.Shell.Content).Title);
    }

    [Fact]
    public async Task The_session_survives_going_to_settings_a_layout_switch_and_coming_back_and_its_prompt_shows_wherever_the_player_is()
    {
        using var app = await ComposedApp.StartAsync();
        await Row(app, MockupScenario.ModdedId).PlayCommand.ExecuteAsync(null).Soon();
        app.Backend.Launch.ReportRunning(_skyrim, MockupScenario.RunningSince);
        var running = Assert.IsType<RunningCardViewModel>(app.Games.InlineContent);

        app.Shell.Navigate(NavDestination.Settings);
        await app.Shell.SetLayoutAsync(ShellLayout.Library);
        app.Shell.Navigate(NavDestination.Games);

        Assert.Same(running, app.Games.InlineContent);
        Assert.Equal(ShellLayout.Library, app.Shell.Layout);
        Assert.True(app.Shell.LaunchFlow!.IsActive(_skyrim));
        AssertPlayIs(app, false);

        await app.Shell.SetLayoutAsync(ShellLayout.Sidebar);
        app.Shell.Navigate(NavDestination.History);
        app.Backend.Launch.ReportExited(_skyrim, MockupScenario.PlayDuration, MockupScenario.NewSaveFiles);

        var postPlay = app.Dialog<PostPlayDialogViewModel>();
        Assert.False(postPlay.IsClosed);
        Assert.Equal(NavDestination.History, app.Shell.Destination);

        postPlay.NotNowCommand.Execute(null);
        app.Shell.Navigate(NavDestination.Games);

        Assert.Null(app.Games.InlineContent);
        Assert.False(app.Shell.LaunchFlow.IsActive(_skyrim));
        AssertPlayIs(app, true);
    }
}
