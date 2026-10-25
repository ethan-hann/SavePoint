using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Features.Help;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.Shell;

public class ShellViewModelTests
{
    // ---- Starting up

    [Fact]
    public async Task Starting_loads_the_games_with_their_profile_counts_and_sizes()
    {
        var rig = await ShellRig.StartAsync();

        Assert.True(rig.Shell.IsLoaded);
        Assert.Equal(["Skyrim Special Edition", "Satisfactory", "The Last Caretaker"], rig.Shell.Games.Select(row => row.Name));
        Assert.Equal([3, 2, 1], rig.Shell.Games.Select(row => row.ProfileCount));
        Assert.Equal(["3 profiles", "2 profiles", "1 profile"], rig.Shell.Games.Select(row => row.ProfileCountText));
        Assert.Equal(
            ["3 profiles \u00b7 41.2 GB", "2 profiles \u00b7 28.7 GB", "1 profile \u00b7 9.4 GB"],
            rig.Shell.Games.Select(row => row.LibrarySubtitle));
        Assert.Equal("Skyrim Special Edition, 3 profiles", rig.Shell.Games[0].AutomationName);
    }

    [Fact]
    public async Task The_first_game_is_selected_at_first()
    {
        var rig = await ShellRig.StartAsync();

        Assert.Equal(SampleScenario.SkyrimId, rig.Shell.SelectedGameRow?.Id);
        Assert.Equal(SampleScenario.SkyrimId, rig.Shell.SelectedGame?.Id);
        Assert.True(rig.Shell.Games[0].IsSelected);
        Assert.False(rig.Shell.Games[1].IsSelected);
    }

    [Fact]
    public async Task The_saved_layout_and_theme_are_applied()
    {
        var rig = await ShellRig.StartAsync();

        Assert.Equal(ShellLayout.Sidebar, rig.Shell.Layout);
        Assert.True(rig.Shell.IsSidebarLayout);
        Assert.False(rig.Shell.IsLibraryLayout);
        Assert.Equal([(ThemePreference.Dark, AccentColorId.Blue)], rig.Theme.Applied);
        Assert.Equal(ShellLayout.Sidebar, rig.Shell.LayoutToggle.Selected?.Layout);
    }

    [Fact]
    public async Task A_saved_library_layout_is_followed()
    {
        var rig = await ShellRig.LibraryLayoutAsync();

        Assert.Equal(ShellLayout.Library, rig.Shell.Layout);
        Assert.True(rig.Shell.IsLibraryLayout);
        Assert.Equal(ShellLayout.Library, rig.Shell.LayoutToggle.Selected?.Layout);
    }

    [Fact]
    public async Task An_empty_library_shows_the_first_run_state_with_the_storage_folder()
    {
        var rig = await ShellRig.StartAsync(FakeBackend.Empty());

        Assert.True(rig.Shell.ShowFirstRun);
        Assert.Same(rig.Shell.FirstRun, rig.Shell.Content);
        Assert.Null(rig.Shell.SelectedGame);
        Assert.Empty(rig.Shell.Games);
        Assert.Equal(@"C:\Users\you\AppData\Local\SavePoint\Storage", rig.Shell.FirstRun.StoragePath);
        Assert.Equal("SavePoint keeps your snapshots in ", rig.Shell.FirstRun.StorageNoteBefore);
        Assert.Equal(". You can change that in Settings.", rig.Shell.FirstRun.StorageNoteAfter);
        Assert.True(rig.Shell.FirstRun.HasStorageNote);
    }

    [Fact]
    public async Task History_is_dimmed_while_the_library_is_empty_and_back_once_it_has_a_game()
    {
        var empty = await ShellRig.StartAsync(FakeBackend.Empty());
        var full = await ShellRig.StartAsync();

        Assert.Equal([true, false, true, true], empty.Shell.Nav.Select(item => item.IsEnabled));
        Assert.Equal([true, true, true, true], full.Shell.Nav.Select(item => item.IsEnabled));

        FakeAssert.Ok(await empty.Backend.Games.AddAsync(new GameDefinition(Id: "new-game", Name: "New Game", Install: new InstallLocation(@"D:\Games\New"))));
        await empty.Shell.RefreshLibraryAsync();

        Assert.True(empty.Shell.Nav[1].IsEnabled);
    }

    [Fact]
    public async Task First_run_shows_only_on_the_games_destination()
    {
        var rig = await ShellRig.StartAsync(FakeBackend.Empty());

        rig.Shell.Navigate(NavDestination.Settings);

        Assert.False(rig.Shell.ShowFirstRun);
        Assert.IsType<StandInScreen>(rig.Shell.Content);
    }

    [Fact]
    public async Task Nothing_is_shown_as_first_run_before_the_library_has_loaded()
    {
        var rig = ShellRig.Create(FakeBackend.Empty());

        Assert.False(rig.Shell.IsLoaded);
        Assert.False(rig.Shell.ShowFirstRun);
        await rig.Shell.InitializeAsync();
        Assert.True(rig.Shell.ShowFirstRun);
    }

    [Fact]
    public async Task Settings_that_cannot_be_read_keep_the_defaults_and_tell_the_user()
    {
        var backend = FakeBackend.Sample();
        backend.FailNext<ISettingsApi>(nameof(ISettingsApi.GetAsync), ErrorInfo.From(new InvalidOperationException("boom")));

        var rig = await ShellRig.StartAsync(backend);

        Assert.Equal(ShellLayout.Sidebar, rig.Shell.Layout);
        Assert.Empty(rig.Theme.Applied);
        var toast = Assert.Single(rig.Toasts.Toasts);
        Assert.Equal(ToastKind.Error, toast.Message.Kind);
        Assert.Equal("Something went wrong. Try again.", toast.Text);
        Assert.Equal(3, rig.Shell.Games.Count);
    }

    [Fact]
    public async Task A_game_list_that_cannot_be_read_fills_the_page_with_the_error_and_Try_again_instead_of_a_blank_screen()
    {
        var backend = FakeBackend.Sample();
        backend.FailNext<IGamesApi>(nameof(IGamesApi.ListAsync), ErrorInfo.From(new InvalidOperationException("boom")));

        var rig = await ShellRig.StartAsync(backend);

        Assert.Empty(rig.Shell.Games);
        Assert.False(rig.Shell.ShowFirstRun);
        var error = Assert.IsType<LibraryErrorViewModel>(rig.Shell.Content);
        Assert.Same(error, rig.Shell.LibraryError);
        Assert.Equal(["Try again"], error.Card.Actions.Select(action => action.Label));
        Assert.True(error.Card.Actions[0].IsPrimary);
        Assert.Empty(rig.Toasts.Toasts);
    }

    [Fact]
    public async Task Try_again_reads_the_library_and_gives_the_page_back()
    {
        var backend = FakeBackend.Sample();
        backend.FailNext<IGamesApi>(nameof(IGamesApi.ListAsync), ErrorInfo.From(new InvalidOperationException("boom")));
        var rig = await ShellRig.StartAsync(backend);
        var error = Assert.IsType<LibraryErrorViewModel>(rig.Shell.Content);

        error.Card.Actions[0].Command.Execute(null);
        await rig.Shell.RefreshLibraryAsync();

        Assert.Null(rig.Shell.LibraryError);
        Assert.Equal(3, rig.Shell.Games.Count);
        Assert.Same(rig.Shell.CurrentScreen, rig.Shell.Content);
    }

    [Fact]
    public async Task A_second_failure_shows_the_error_again_and_a_library_already_read_keeps_its_page()
    {
        var backend = FakeBackend.Sample();
        backend.FailNext<IGamesApi>(nameof(IGamesApi.ListAsync), ErrorInfo.From(new InvalidOperationException("boom")));
        var rig = await ShellRig.StartAsync(backend);
        rig.Backend.FailNext<IGamesApi>(nameof(IGamesApi.ListAsync), ErrorInfo.From(new InvalidOperationException("boom")));

        await rig.Shell.RefreshLibraryAsync();

        Assert.IsType<LibraryErrorViewModel>(rig.Shell.Content);

        await rig.Shell.RefreshLibraryAsync();
        Assert.Null(rig.Shell.LibraryError);

        rig.Backend.FailNext<IGamesApi>(nameof(IGamesApi.ListAsync), ErrorInfo.From(new InvalidOperationException("boom")));
        await rig.Shell.RefreshLibraryAsync();

        Assert.Null(rig.Shell.LibraryError);
        Assert.Equal(3, rig.Shell.Games.Count);
    }

    // ---- Navigation

    [Fact]
    public async Task The_navigation_has_games_history_activity_and_settings_with_games_selected()
    {
        var rig = await ShellRig.StartAsync();

        Assert.Equal(["Games", "History", "Activity", "Settings"], rig.Shell.Nav.Select(item => item.Label));
        Assert.Equal([NavDestination.Games, NavDestination.History, NavDestination.Activity, NavDestination.Settings], rig.Shell.Nav.Select(item => item.Destination));
        Assert.Equal(NavDestination.Games, rig.Shell.Destination);
        Assert.Same(rig.Shell.Nav[0], rig.Shell.SelectedNav);
    }

    [Fact]
    public async Task Navigating_switches_the_screen_and_the_selected_item()
    {
        var rig = await ShellRig.StartAsync();

        rig.Shell.Navigate(NavDestination.History);

        Assert.Equal(NavDestination.History, rig.Shell.Destination);
        Assert.Equal(NavDestination.History, rig.Shell.CurrentScreen?.Destination);
        Assert.Same(rig.Shell.Nav[1], rig.Shell.SelectedNav);
        Assert.Same(rig.Shell.CurrentScreen, rig.Shell.Content);
    }

    [Fact]
    public async Task Choosing_a_navigation_item_navigates()
    {
        var rig = await ShellRig.StartAsync();

        rig.Shell.SelectedNav = rig.Shell.Nav[3];

        Assert.Equal(NavDestination.Settings, rig.Shell.Destination);
    }

    [Fact]
    public async Task Moving_the_game_selection_leaves_the_current_screen_alone()
    {
        var rig = await ShellRig.StartAsync();
        rig.Shell.Navigate(NavDestination.Settings);

        rig.Shell.SelectedGameRow = rig.Shell.Games[1];

        Assert.Equal(NavDestination.Settings, rig.Shell.Destination);
        Assert.Equal(SampleScenario.SatisfactoryId, rig.Shell.SelectedGame?.Id);
    }

    [Fact]
    public async Task Opening_the_selected_game_from_settings_shows_the_games_screen()
    {
        var rig = await ShellRig.StartAsync();
        rig.Shell.Navigate(NavDestination.Settings);

        rig.Shell.SelectGame(SampleScenario.SkyrimId);

        Assert.Equal(NavDestination.Games, rig.Shell.Destination);
    }

    [Fact]
    public async Task Opening_a_game_while_the_help_page_is_open_closes_the_page()
    {
        var rig = await ShellRig.StartAsync();
        rig.Shell.Navigate(NavDestination.Settings);
        rig.Shell.ShowHelp();

        rig.Shell.SelectGame(SampleScenario.SkyrimId);
        rig.Shell.Navigate(NavDestination.Settings);

        Assert.IsNotType<HowItWorksViewModel>(rig.Shell.Content);
    }

    [Fact]
    public async Task Opening_a_game_while_the_help_page_is_open_over_games_closes_the_page()
    {
        var rig = await ShellRig.StartAsync();
        rig.Shell.ShowHelp();

        rig.Shell.SelectGame(SampleScenario.SatisfactoryId);

        Assert.IsNotType<HowItWorksViewModel>(rig.Shell.Content);
        Assert.Equal(SampleScenario.SatisfactoryId, rig.Shell.SelectedGame?.Id);
    }

    [Fact]
    public async Task Clearing_the_navigation_selection_changes_nothing()
    {
        var rig = await ShellRig.StartAsync();

        rig.Shell.SelectedNav = null;

        Assert.Equal(NavDestination.Games, rig.Shell.Destination);
        Assert.Same(rig.Shell.Nav[0], rig.Shell.SelectedNav);
    }

    [Fact]
    public async Task A_shell_without_a_screen_for_every_destination_refuses_to_start_and_names_the_one_missing()
    {
        var rig = ShellRig.Create(screens: [new RecordingScreenFactory(NavDestination.Games), new RecordingScreenFactory(NavDestination.Settings)], standIns: false);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(rig.Shell.InitializeAsync);

        Assert.Contains("History", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_registered_screen_is_created_once_with_the_shell_as_its_context()
    {
        var games = new RecordingScreenFactory(NavDestination.Games);
        var settings = new RecordingScreenFactory(NavDestination.Settings);

        var rig = await ShellRig.StartAsync(screens: [games, settings]);
        rig.Shell.Navigate(NavDestination.Settings);
        rig.Shell.Navigate(NavDestination.Games);
        rig.Shell.Navigate(NavDestination.Settings);

        Assert.Equal(1, games.Created);
        Assert.Equal(1, settings.Created);
        Assert.Same(rig.Shell, games.Context);
    }

    [Fact]
    public async Task A_screen_is_told_each_time_it_is_shown()
    {
        var games = new RecordingScreenFactory(NavDestination.Games);
        var history = new RecordingScreenFactory(NavDestination.History);
        var rig = await ShellRig.StartAsync(screens: [games, history]);

        Assert.Equal(1, games.Screen!.ShownCount);
        Assert.Equal(0, history.Screen!.ShownCount);

        rig.Shell.Navigate(NavDestination.History);
        rig.Shell.Navigate(NavDestination.Games);

        Assert.Equal(2, games.Screen.ShownCount);
        Assert.Equal(1, history.Screen.ShownCount);
    }

    [Fact]
    public async Task Navigating_to_where_the_shell_already_is_does_not_show_the_screen_again()
    {
        var games = new RecordingScreenFactory(NavDestination.Games);
        var rig = await ShellRig.StartAsync(screens: [games]);

        rig.Shell.Navigate(NavDestination.Games);

        Assert.Equal(1, games.Screen!.ShownCount);
    }

    // ---- Layout

    [Fact]
    public async Task Choosing_the_library_layout_saves_every_setting_with_only_the_layout_changed()
    {
        var rig = await ShellRig.StartAsync();

        await rig.Shell.SetLayoutAsync(ShellLayout.Library);

        Assert.Equal(ShellLayout.Library, rig.Shell.Layout);
        Assert.Equal(ShellLayout.Library, rig.Shell.LayoutToggle.Selected?.Layout);
        var save = Assert.Single(rig.Backend.Calls.To<ISettingsApi>(nameof(ISettingsApi.SaveAsync)));
        Assert.Equal(SampleScenario.Settings with { Layout = ShellLayout.Library }, save.Args[0]);
    }

    [Fact]
    public async Task Choosing_a_segment_of_the_toggle_switches_the_layout()
    {
        var rig = await ShellRig.StartAsync();

        rig.Shell.LayoutToggle.Selected = rig.Shell.LayoutToggle.Options[1];

        Assert.Equal(ShellLayout.Library, rig.Shell.Layout);
        Assert.Single(rig.Backend.Calls.To<ISettingsApi>(nameof(ISettingsApi.SaveAsync)));
    }

    [Fact]
    public async Task A_layout_that_cannot_be_saved_is_put_back_and_the_user_is_told()
    {
        var rig = await ShellRig.StartAsync();
        rig.Backend.FailNext<ISettingsApi>(nameof(ISettingsApi.SaveAsync), ErrorInfo.From(new InvalidOperationException("boom")));

        await rig.Shell.SetLayoutAsync(ShellLayout.Library);

        Assert.Equal(ShellLayout.Sidebar, rig.Shell.Layout);
        Assert.Equal(ShellLayout.Sidebar, rig.Shell.LayoutToggle.Selected?.Layout);
        Assert.Equal(ToastKind.Error, Assert.Single(rig.Toasts.Toasts).Message.Kind);
    }

    [Fact]
    public async Task Choosing_the_layout_already_in_use_saves_nothing()
    {
        var rig = await ShellRig.StartAsync();

        await rig.Shell.SetLayoutAsync(ShellLayout.Sidebar);

        Assert.Empty(rig.Backend.Calls.To<ISettingsApi>(nameof(ISettingsApi.SaveAsync)));
    }

    [Fact]
    public async Task Two_layout_changes_in_a_row_each_save_on_top_of_the_last()
    {
        var rig = await ShellRig.StartAsync();

        await rig.Shell.SetLayoutAsync(ShellLayout.Library);
        await rig.Shell.SetLayoutAsync(ShellLayout.Sidebar);

        var saves = rig.Backend.Calls.To<ISettingsApi>(nameof(ISettingsApi.SaveAsync));
        Assert.Equal([ShellLayout.Library, ShellLayout.Sidebar], saves.Select(call => ((AppSettings)call.Args[0]!).Layout));
        Assert.Equal(SampleScenario.Settings, saves[1].Args[0]);
    }

    [Fact]
    public async Task Changing_the_layout_keeps_the_same_screens_and_content()
    {
        var games = new RecordingScreenFactory(NavDestination.Games);
        var rig = await ShellRig.StartAsync(screens: [games]);
        var before = rig.Shell.Content;

        await rig.Shell.SetLayoutAsync(ShellLayout.Library);

        Assert.Same(before, rig.Shell.Content);
        Assert.Equal(1, games.Created);
        Assert.Equal(1, games.Screen!.ShownCount);
    }

    [Fact]
    public async Task Settings_saved_elsewhere_move_the_layout_and_the_theme()
    {
        var rig = await ShellRig.StartAsync();

        rig.Shell.SettingsChanged(SampleScenario.Settings with { Layout = ShellLayout.Library, Theme = ThemePreference.Light, AccentColorId = AccentColorId.Crimson });

        Assert.Equal(ShellLayout.Library, rig.Shell.Layout);
        Assert.Equal(ShellLayout.Library, rig.Shell.LayoutToggle.Selected?.Layout);
        Assert.Equal((ThemePreference.Light, AccentColorId.Crimson), rig.Theme.Applied[^1]);
        Assert.Empty(rig.Backend.Calls.To<ISettingsApi>(nameof(ISettingsApi.SaveAsync)));
    }

    [Fact]
    public async Task Settings_the_launch_flow_saved_are_followed_like_any_others()
    {
        var flow = new FakeLaunchFlow();
        var rig = await ShellRig.StartAsync(launchFlow: flow);

        flow.RaiseSettingsSaved(SampleScenario.Settings with { Layout = ShellLayout.Library, Theme = ThemePreference.Light });

        Assert.Equal(ShellLayout.Library, rig.Shell.Layout);
        Assert.Equal((ThemePreference.Light, AccentColorId.Blue), rig.Theme.Applied[^1]);
    }

    [Fact]
    public async Task A_disposed_shell_stops_following_the_launch_flows_settings()
    {
        var flow = new FakeLaunchFlow();
        var rig = await ShellRig.StartAsync(launchFlow: flow);

        rig.Shell.Dispose();

        Assert.Equal(0, flow.SettingsSavedSubscribers);
    }

    [Fact]
    public async Task Choosing_a_layout_keeps_what_another_writer_saved_since_the_shell_last_looked()
    {
        var rig = await ShellRig.StartAsync();
        FakeAssert.Ok(await rig.Backend.Settings.SaveAsync(SampleScenario.Settings with { ConfirmSessionCapture = false, SkipConfirmWhenOnlySaveChanges = true }));

        await rig.Shell.SetLayoutAsync(ShellLayout.Library);

        var stored = FakeAssert.Ok(await rig.Backend.Settings.GetAsync(TestContext.Current.CancellationToken));
        Assert.Equal(ShellLayout.Library, stored.Layout);
        Assert.False(stored.ConfirmSessionCapture);
        Assert.True(stored.SkipConfirmWhenOnlySaveChanges);
    }

    [Fact]
    public async Task The_shelf_shows_only_in_the_library_layout_on_games_with_games_and_no_pane()
    {
        var rig = await ShellRig.LibraryLayoutAsync();

        Assert.True(rig.Shell.ShowShelf);

        rig.Shell.Navigate(NavDestination.History);
        Assert.False(rig.Shell.ShowShelf);

        rig.Shell.Navigate(NavDestination.Games);
        Assert.True(rig.Shell.ShowShelf);

        rig.Shell.ShowPane(new object());
        Assert.False(rig.Shell.ShowShelf);

        var sidebar = await ShellRig.StartAsync();
        Assert.False(sidebar.Shell.ShowShelf);
    }

    [Fact]
    public async Task The_shelf_is_hidden_when_the_library_is_empty()
    {
        var scenario = ScenarioBuilder.Empty().WithSettings(SampleScenario.Settings with { Layout = ShellLayout.Library });
        var rig = await ShellRig.StartAsync(new FakeBackend(scenario));

        Assert.False(rig.Shell.ShowShelf);
        Assert.True(rig.Shell.ShowFirstRun);
    }

    // ---- Selection

    [Fact]
    public async Task Selecting_a_game_selects_it_and_deselects_the_other()
    {
        var rig = await ShellRig.StartAsync();

        rig.Shell.SelectGame(SampleScenario.SatisfactoryId);

        Assert.Equal(SampleScenario.SatisfactoryId, rig.Shell.SelectedGame?.Id);
        Assert.Equal([false, true, false], rig.Shell.Games.Select(row => row.IsSelected));
    }

    [Fact]
    public async Task Selecting_a_game_announces_the_change_on_the_context()
    {
        var rig = await ShellRig.StartAsync();

        var changes = rig.Changes(rig.Shell, () => rig.Shell.SelectGame(SampleScenario.CaretakerId));

        Assert.Contains("SelectedGame", changes);
    }

    [Fact]
    public async Task Selecting_a_game_that_is_not_there_is_ignored()
    {
        var rig = await ShellRig.StartAsync();

        rig.Shell.SelectGame("no-such-game");

        Assert.Equal(SampleScenario.SkyrimId, rig.Shell.SelectedGame?.Id);
    }

    [Fact]
    public async Task Setting_the_selected_row_selects_that_game()
    {
        var rig = await ShellRig.StartAsync();

        rig.Shell.SelectedGameRow = rig.Shell.Games[2];

        Assert.Equal(SampleScenario.CaretakerId, rig.Shell.SelectedGame?.Id);
        Assert.True(rig.Shell.Games[2].IsSelected);
        Assert.False(rig.Shell.Games[0].IsSelected);
    }

    [Fact]
    public async Task Selecting_a_game_while_activity_is_shown_stays_on_activity()
    {
        var rig = await ShellRig.StartAsync();
        rig.Shell.Navigate(NavDestination.Activity);

        rig.Shell.SelectGame(SampleScenario.CaretakerId);

        Assert.Equal(NavDestination.Activity, rig.Shell.Destination);
        Assert.Equal(SampleScenario.CaretakerId, rig.Shell.SelectedGame?.Id);
    }

    [Fact]
    public async Task Showing_activity_for_a_game_selects_it_tells_the_screen_and_then_navigates()
    {
        var activity = new RecordingScreenFactory(NavDestination.Activity);
        var rig = await ShellRig.StartAsync(screens: [activity]);
        var seen = new List<string>();
        rig.Shell.ActivityRequested += (_, request) => seen.Add($"request:{request.GameId}:shown={activity.Screen!.ShownCount}:selected={rig.Shell.SelectedGame?.Id}");

        rig.Shell.ShowActivity(SampleScenario.SatisfactoryId);

        // The screen hears the request before it is shown, and the game is already selected.
        Assert.Equal([$"request:{SampleScenario.SatisfactoryId}:shown=0:selected={SampleScenario.SatisfactoryId}"], seen);
        Assert.Equal(NavDestination.Activity, rig.Shell.Destination);
        Assert.Equal(1, activity.Screen!.ShownCount);
    }

    [Fact]
    public async Task Showing_activity_for_every_game_keeps_the_selected_game()
    {
        var rig = await ShellRig.StartAsync();
        var requests = new List<ActivityRequest>();
        rig.Shell.ActivityRequested += (_, request) => requests.Add(request);

        rig.Shell.ShowActivity(null);

        Assert.Equal([new ActivityRequest(null)], requests);
        Assert.Equal(NavDestination.Activity, rig.Shell.Destination);
        Assert.Equal(SampleScenario.SkyrimId, rig.Shell.SelectedGame?.Id);
    }

    [Fact]
    public async Task Showing_activity_while_it_is_already_shown_still_tells_the_screen()
    {
        var rig = await ShellRig.StartAsync();
        rig.Shell.Navigate(NavDestination.Activity);
        var requests = new List<ActivityRequest>();
        rig.Shell.ActivityRequested += (_, request) => requests.Add(request);

        rig.Shell.ShowActivity(SampleScenario.CaretakerId);

        Assert.Equal([new ActivityRequest(SampleScenario.CaretakerId)], requests);
        Assert.Equal(NavDestination.Activity, rig.Shell.Destination);
    }

    [Fact]
    public async Task Showing_activity_for_a_game_that_is_not_in_the_library_shows_every_game()
    {
        var rig = await ShellRig.StartAsync();
        var requests = new List<ActivityRequest>();
        rig.Shell.ActivityRequested += (_, request) => requests.Add(request);

        rig.Shell.ShowActivity("no-such-game");

        Assert.Equal([new ActivityRequest(null)], requests);
        Assert.Equal(NavDestination.Activity, rig.Shell.Destination);
    }

    [Fact]
    public async Task Selecting_a_game_from_settings_goes_to_games_but_history_stays_on_history()
    {
        var rig = await ShellRig.StartAsync();
        rig.Shell.Navigate(NavDestination.Settings);

        rig.Shell.SelectGame(SampleScenario.SatisfactoryId);

        Assert.Equal(NavDestination.Games, rig.Shell.Destination);

        rig.Shell.Navigate(NavDestination.History);
        rig.Shell.SelectGame(SampleScenario.CaretakerId);

        Assert.Equal(NavDestination.History, rig.Shell.Destination);
        Assert.Equal(SampleScenario.CaretakerId, rig.Shell.SelectedGame?.Id);
    }

    [Fact]
    public async Task The_context_lists_the_definitions_in_order()
    {
        var rig = await ShellRig.StartAsync();

        IShellContext context = rig.Shell;

        Assert.Equal([SampleScenario.SkyrimId, SampleScenario.SatisfactoryId, SampleScenario.CaretakerId], context.Games.Select(game => game.Id));
    }

    // ---- Keeping the library current

    [Fact]
    public async Task A_finished_capture_updates_the_counts_without_being_asked()
    {
        var rig = await ShellRig.StartAsync();
        var created = FakeAssert.Ok(await rig.Backend.Profiles.CreateAsync(SampleScenario.SkyrimId, "A new profile", null, "First point"));
        Assert.Equal(3, rig.Shell.Games[0].ProfileCount);

        rig.Backend.Jobs.Complete(created.JobId);

        Assert.Equal(4, rig.Shell.Games[0].ProfileCount);
        Assert.Equal("4 profiles", rig.Shell.Games[0].ProfileCountText);
    }

    [Fact]
    public async Task A_refresh_keeps_the_row_of_every_game_that_is_still_there_and_changes_nothing_in_the_list()
    {
        var rig = await ShellRig.StartAsync();
        var before = rig.Shell.Games.ToList();
        var collectionChanges = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
        rig.Shell.Games.CollectionChanged += (_, e) => collectionChanges.Add(e.Action);

        await rig.Shell.RefreshLibraryAsync();

        Assert.True(before.SequenceEqual(rig.Shell.Games, ReferenceEqualityComparer.Instance as IEqualityComparer<GameRowViewModel>), "A refresh replaced a row that was still wanted.");
        Assert.Empty(collectionChanges);
    }

    [Fact]
    public async Task A_refresh_updates_a_rows_counts_in_place_and_adds_or_removes_only_what_changed()
    {
        var rig = await ShellRig.StartAsync();
        var skyrim = rig.Shell.Games[0];
        var satisfactory = rig.Shell.Games[1];
        var caretaker = rig.Shell.Games[2];
        var collectionChanges = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
        rig.Shell.Games.CollectionChanged += (_, e) => collectionChanges.Add(e.Action);
        FakeAssert.Ok(await rig.Backend.Profiles.CreateAsync(SampleScenario.SkyrimId, "A new profile", null, "First point"));
        FakeAssert.Ok(await rig.Backend.Games.DeleteAsync(SampleScenario.SatisfactoryId));

        await rig.Shell.RefreshLibraryAsync();

        Assert.Same(skyrim, rig.Shell.Games[0]);
        Assert.Same(caretaker, rig.Shell.Games[1]);
        Assert.Equal(2, rig.Shell.Games.Count);
        Assert.Equal("4 profiles", skyrim.ProfileCountText);
        Assert.Equal([System.Collections.Specialized.NotifyCollectionChangedAction.Remove], collectionChanges);
        Assert.DoesNotContain(satisfactory, rig.Shell.Games);
    }

    [Fact]
    public async Task A_game_added_or_renamed_since_the_last_read_changes_only_its_own_row()
    {
        var rig = await ShellRig.StartAsync();
        var before = rig.Shell.Games.ToList();
        FakeAssert.Ok(await rig.Backend.Games.AddAsync(new GameDefinition(Id: "new-game", Name: "New Game", Install: new InstallLocation(@"D:\Games\New"))));
        FakeAssert.Ok(await rig.Backend.Games.UpdateAsync(SampleScenario.Satisfactory with { Name = "Satisfactory 1.1" }));
        var collectionChanges = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
        rig.Shell.Games.CollectionChanged += (_, e) => collectionChanges.Add(e.Action);

        await rig.Shell.RefreshLibraryAsync();

        Assert.Equal(4, rig.Shell.Games.Count);
        Assert.Same(before[0], rig.Shell.Games[0]);
        Assert.Same(before[1], rig.Shell.Games[1]);
        Assert.Same(before[2], rig.Shell.Games[2]);
        Assert.Equal("Satisfactory 1.1", before[1].Name);
        Assert.Equal("Satisfactory 1.1, 2 profiles", before[1].AutomationName);
        Assert.Equal("New Game", rig.Shell.Games[3].Name);
        Assert.Equal([System.Collections.Specialized.NotifyCollectionChangedAction.Add], collectionChanges);
    }

    [Fact]
    public async Task A_finished_optimization_updates_the_sizes()
    {
        var rig = await ShellRig.StartAsync();
        var job = FakeAssert.Ok(await rig.Backend.Games.OptimizeStorageAsync(SampleScenario.SkyrimId));

        rig.Backend.Jobs.Complete(job.JobId);

        Assert.Single(rig.Backend.Calls.To<ISettingsApi>(nameof(ISettingsApi.GetStorageSummaryAsync)).Skip(1));
    }

    [Fact]
    public async Task A_launch_job_finishing_does_not_reload_the_library()
    {
        var rig = await ShellRig.StartAsync();
        var before = rig.Backend.Calls.To<IGamesApi>(nameof(IGamesApi.ListAsync)).Count;
        FakeAssert.Ok(await rig.Backend.Launch.StartAsync(SampleScenario.SkyrimId, new LaunchTarget.ProfileTip(SampleScenario.ModdedId)));

        rig.Backend.Launch.ReportRunning(SampleScenario.SkyrimId);

        Assert.Equal(before, rig.Backend.Calls.To<IGamesApi>(nameof(IGamesApi.ListAsync)).Count);
    }

    [Fact]
    public async Task A_running_job_does_not_reload_the_library_until_it_finishes()
    {
        var rig = await ShellRig.StartAsync();
        var before = rig.Backend.Calls.To<IGamesApi>(nameof(IGamesApi.ListAsync)).Count;

        var job = FakeAssert.Ok(await rig.Backend.Profiles.CaptureAsync(SampleScenario.SkyrimId, SampleScenario.ModdedId, "p"));
        rig.Backend.Jobs.ReportProgress(job.JobId, new JobProgress(FilesDone: 1));

        Assert.Equal(before, rig.Backend.Calls.To<IGamesApi>(nameof(IGamesApi.ListAsync)).Count);
    }

    [Fact]
    public async Task Refreshing_after_the_selected_game_is_deleted_selects_the_first_remaining_game()
    {
        var rig = await ShellRig.StartAsync();
        FakeAssert.Ok(await rig.Backend.Games.DeleteAsync(SampleScenario.SkyrimId));

        await rig.Shell.RefreshLibraryAsync();

        Assert.Equal(SampleScenario.SatisfactoryId, rig.Shell.SelectedGame?.Id);
        Assert.Equal(2, rig.Shell.Games.Count);
        Assert.True(rig.Shell.Games[0].IsSelected);
    }

    [Fact]
    public async Task Refreshing_after_every_game_is_gone_shows_the_first_run_state()
    {
        var rig = await ShellRig.StartAsync();
        foreach (var id in new[] { SampleScenario.SkyrimId, SampleScenario.SatisfactoryId, SampleScenario.CaretakerId })
        {
            FakeAssert.Ok(await rig.Backend.Games.DeleteAsync(id));
        }

        await rig.Shell.RefreshLibraryAsync();

        Assert.Null(rig.Shell.SelectedGame);
        Assert.True(rig.Shell.ShowFirstRun);
        Assert.Same(rig.Shell.FirstRun, rig.Shell.Content);
    }

    [Fact]
    public async Task A_reload_keeps_the_selection_when_the_game_is_still_there()
    {
        var rig = await ShellRig.StartAsync();
        rig.Shell.SelectGame(SampleScenario.CaretakerId);

        await rig.Shell.RefreshLibraryAsync();

        Assert.Equal(SampleScenario.CaretakerId, rig.Shell.SelectedGame?.Id);
        Assert.True(rig.Shell.Games[2].IsSelected);
    }

    [Fact]
    public async Task A_reload_that_fails_keeps_the_old_library_and_tells_the_user()
    {
        var rig = await ShellRig.StartAsync();
        rig.Backend.FailNext<IGamesApi>(nameof(IGamesApi.ListAsync), ErrorInfo.From(new InvalidOperationException("boom")));

        await rig.Shell.RefreshLibraryAsync();

        Assert.Equal(3, rig.Shell.Games.Count);
        Assert.Equal(ToastKind.Error, Assert.Single(rig.Toasts.Toasts).Message.Kind);
    }

    [Fact]
    public async Task Counts_that_cannot_be_read_leave_the_previous_counts_in_place()
    {
        var rig = await ShellRig.StartAsync();
        rig.Backend.FailNext<IProfilesApi>(nameof(IProfilesApi.ListAsync), ErrorInfo.From(new InvalidOperationException("boom")));

        await rig.Shell.RefreshLibraryAsync();

        Assert.Equal([3, 2, 1], rig.Shell.Games.Select(row => row.ProfileCount));
        Assert.Single(rig.Toasts.Toasts);
    }

    [Fact]
    public async Task Sizes_that_cannot_be_read_are_missing_not_zero()
    {
        var backend = FakeBackend.Sample();
        backend.FailNext<ISettingsApi>(nameof(ISettingsApi.GetStorageSummaryAsync), ErrorInfo.From(new InvalidOperationException("boom")));

        var rig = await ShellRig.StartAsync(backend);

        Assert.All(rig.Shell.Games, row => Assert.Null(row.Bytes));
        Assert.Equal("3 profiles", rig.Shell.Games[0].LibrarySubtitle);
    }

    [Fact]
    public async Task Each_reload_tells_the_screens()
    {
        var rig = await ShellRig.StartAsync();
        var raised = 0;
        rig.Shell.LibraryRefreshed += (_, _) => raised++;

        await rig.Shell.RefreshLibraryAsync();
        await rig.Shell.RefreshLibraryAsync();

        Assert.Equal(2, raised);
    }

    // ---- Responsive

    [Theory]
    [InlineData(1040, false, 236)]
    [InlineData(900, false, 236)]
    [InlineData(899.5, true, 64)]
    [InlineData(760, true, 64)]
    public async Task The_sidebar_collapses_to_an_icon_rail_below_900_pixels(double width, bool compact, double railWidth)
    {
        var rig = await ShellRig.StartAsync();

        rig.Shell.SetWindowWidth(width);

        Assert.Equal(compact, rig.Shell.IsCompact);
        Assert.Equal(railWidth, rig.Shell.RailWidth);
    }

    [Theory]
    [InlineData(660, false)]
    [InlineData(560, false)]
    [InlineData(559.5, true)]
    [InlineData(432, true)]
    public async Task A_short_window_compacts_the_shelf_below_560_pixels(double height, bool isShort)
    {
        var rig = await ShellRig.StartAsync();

        rig.Shell.SetWindowHeight(height);

        Assert.Equal(isShort, rig.Shell.IsShort);
    }

    [Fact]
    public async Task A_collapsed_rail_hides_names_and_offers_them_as_tooltips()
    {
        var rig = await ShellRig.StartAsync();
        Assert.All(rig.Shell.Nav, item => Assert.Null(item.ToolTipText));
        Assert.All(rig.Shell.Games, row => Assert.Null(row.ToolTipText));

        rig.Shell.SetWindowWidth(700);

        Assert.All(rig.Shell.Nav, item => Assert.False(item.ShowLabel));
        Assert.Equal(["Games", "History", "Activity", "Settings"], rig.Shell.Nav.Select(item => item.ToolTipText));
        Assert.All(rig.Shell.Games, row => Assert.False(row.ShowLabels));
        Assert.Equal("Skyrim Special Edition, 3 profiles", rig.Shell.Games[0].ToolTipText);

        rig.Shell.SetWindowWidth(1000);

        Assert.All(rig.Shell.Nav, item => Assert.True(item.ShowLabel));
        Assert.All(rig.Shell.Games, row => Assert.Null(row.ToolTipText));
    }

    [Fact]
    public async Task Rows_loaded_while_the_rail_is_collapsed_start_collapsed()
    {
        var rig = await ShellRig.StartAsync();
        rig.Shell.SetWindowWidth(700);

        await rig.Shell.RefreshLibraryAsync();

        Assert.All(rig.Shell.Games, row => Assert.False(row.ShowLabels));
    }

    [Fact]
    public async Task Resizing_within_the_same_state_raises_nothing()
    {
        var rig = await ShellRig.StartAsync();
        rig.Shell.SetWindowWidth(1040);

        var changes = rig.Changes(rig.Shell, () => rig.Shell.SetWindowWidth(1200));

        Assert.DoesNotContain(nameof(ShellViewModel.IsCompact), changes);
        Assert.DoesNotContain(nameof(ShellViewModel.RailWidth), changes);
    }

    // ---- Full panes

    [Fact]
    public async Task A_pane_replaces_the_screen_until_it_is_closed()
    {
        var rig = await ShellRig.StartAsync();
        var pane = new object();

        rig.Shell.ShowPane(pane);

        Assert.Same(pane, rig.Shell.Content);

        rig.Shell.ClosePane();

        Assert.Same(rig.Shell.CurrentScreen, rig.Shell.Content);
    }

    [Fact]
    public async Task A_pane_that_is_closed_or_replaced_is_disposed()
    {
        var rig = await ShellRig.StartAsync();
        var first = new DisposablePane();
        var second = new DisposablePane();

        rig.Shell.ShowPane(first);
        Assert.Equal(0, first.Disposed);
        rig.Shell.ShowPane(second);
        Assert.Equal(1, first.Disposed);
        Assert.Equal(0, second.Disposed);
        rig.Shell.ClosePane();

        Assert.Equal(1, first.Disposed);
        Assert.Equal(1, second.Disposed);
    }

    [Fact]
    public async Task The_same_pane_shown_again_is_not_disposed()
    {
        var rig = await ShellRig.StartAsync();
        var pane = new DisposablePane();

        rig.Shell.ShowPane(pane);
        rig.Shell.ShowPane(pane);

        Assert.Equal(0, pane.Disposed);
    }

    [Fact]
    public async Task A_pane_still_open_when_the_shell_ends_is_disposed()
    {
        var rig = await ShellRig.StartAsync();
        var pane = new DisposablePane();
        rig.Shell.ShowPane(pane);

        rig.Shell.Dispose();

        Assert.Equal(1, pane.Disposed);
    }

    private sealed class DisposablePane : IDisposable
    {
        public int Disposed { get; private set; }

        public void Dispose() => Disposed++;
    }

    [Fact]
    public async Task A_pane_belongs_to_the_destination_it_was_opened_from()
    {
        var rig = await ShellRig.StartAsync();
        var pane = new object();
        rig.Shell.ShowPane(pane);

        rig.Shell.Navigate(NavDestination.Settings);
        Assert.NotSame(pane, rig.Shell.Content);

        rig.Shell.Navigate(NavDestination.Games);
        Assert.Same(pane, rig.Shell.Content);
    }

    [Fact]
    public async Task A_pane_keeps_the_first_run_state_out_of_the_way()
    {
        var rig = await ShellRig.StartAsync(FakeBackend.Empty());
        var pane = new object();

        rig.Shell.ShowPane(pane);

        Assert.False(rig.Shell.ShowFirstRun);
        Assert.Same(pane, rig.Shell.Content);

        rig.Shell.ClosePane();

        Assert.True(rig.Shell.ShowFirstRun);
    }

    // ---- Adding a game

    [Fact]
    public async Task Asking_to_add_a_game_goes_to_games_and_asks_the_editor_once()
    {
        var rig = await ShellRig.StartAsync();
        rig.Shell.Navigate(NavDestination.Settings);
        var requests = new List<AddGameRequest>();
        rig.Shell.AddGameRequested += (_, request) => requests.Add(request);

        rig.Shell.AddGameCommand.Execute(null);

        Assert.Equal(NavDestination.Games, rig.Shell.Destination);
        var request = Assert.Single(requests);
        Assert.Null(request.Prefill);
    }

    [Fact]
    public async Task The_first_run_button_asks_for_the_editor_too()
    {
        var rig = await ShellRig.StartAsync(FakeBackend.Empty());
        var requests = new List<AddGameRequest>();
        rig.Shell.AddGameRequested += (_, request) => requests.Add(request);

        rig.Shell.FirstRun.AddFirstGameCommand.Execute(null);

        Assert.Single(requests);
    }

    [Fact]
    public async Task Starting_from_a_template_offers_the_built_in_templates_and_passes_a_fresh_draft_to_the_editor()
    {
        var rig = await ShellRig.StartAsync(FakeBackend.Empty());
        var requests = new List<AddGameRequest>();
        rig.Shell.AddGameRequested += (_, request) => requests.Add(request);

        var pending = ((IAsyncRelayCommand)rig.Shell.FirstRun.StartFromTemplateCommand).ExecuteAsync(null);
        var picker = Assert.IsType<TemplatePickerViewModel>(Assert.Single(rig.Dialogs.Shown));

        Assert.Equal(rig.Backend.Games.Templates.Select(template => template.DisplayName), picker.Templates.Select(item => item.Name));
        picker.Templates[0].Choose.Execute(null);
        await pending;

        var draft = Assert.Single(requests).Prefill!;
        Assert.Equal(rig.Backend.Games.Templates[0].Definition.Name, draft.Name);
        Assert.NotEqual(rig.Backend.Games.Templates[0].Definition.Id, draft.Id);
        Assert.Empty(rig.Backend.Calls.To<IGamesApi>(nameof(IGamesApi.AddAsync)));
    }

    [Fact]
    public async Task Starting_from_a_file_imports_it_and_passes_the_definition_to_the_editor()
    {
        var definition = new GameDefinition(Id: "imported-1", Name: "Imported Game", Install: new InstallLocation(@"D:\Games\Imported"));
        var scenario = ScenarioBuilder.Empty().WithImport(@"C:\Downloads\imported.json", definition);
        var rig = await ShellRig.StartAsync(new FakeBackend(scenario));
        rig.Dialogs.AnswerOpenFile(@"C:\Downloads\imported.json");
        var requests = new List<AddGameRequest>();
        rig.Shell.AddGameRequested += (_, request) => requests.Add(request);

        var pending = ((IAsyncRelayCommand)rig.Shell.FirstRun.StartFromTemplateCommand).ExecuteAsync(null);
        var picker = (TemplatePickerViewModel)rig.Dialogs.Shown[0];
        picker.OpenFileCommand.Execute(null);
        await pending;

        Assert.Equal(definition, Assert.Single(requests).Prefill);
        var pick = Assert.Single(rig.Dialogs.OpenFilePicks);
        Assert.Contains(".json", pick.Filters!.SelectMany(filter => filter.Patterns).First());
    }

    [Fact]
    public async Task Backing_out_of_the_picker_or_the_file_dialog_does_nothing()
    {
        var rig = await ShellRig.StartAsync(FakeBackend.Empty());
        var requests = new List<AddGameRequest>();
        rig.Shell.AddGameRequested += (_, request) => requests.Add(request);

        var cancelled = ((IAsyncRelayCommand)rig.Shell.FirstRun.StartFromTemplateCommand).ExecuteAsync(null);
        ((TemplatePickerViewModel)rig.Dialogs.Shown[0]).RequestDismiss();
        await cancelled;

        var noFile = ((IAsyncRelayCommand)rig.Shell.FirstRun.StartFromTemplateCommand).ExecuteAsync(null);
        ((TemplatePickerViewModel)rig.Dialogs.Shown[1]).OpenFileCommand.Execute(null);
        await noFile;

        Assert.Empty(requests);
        Assert.Empty(rig.Backend.Calls.To<IGamesApi>(nameof(IGamesApi.ImportDefinitionAsync)));
        Assert.Empty(rig.Toasts.Toasts);
    }

    [Fact]
    public async Task A_file_that_cannot_be_imported_tells_the_user_and_opens_no_editor()
    {
        var scenario = ScenarioBuilder.Empty().WithImport(@"C:\Downloads\bad.json", new GameDefinition(Name: "x"));
        var rig = await ShellRig.StartAsync(new FakeBackend(scenario));
        rig.Dialogs.AnswerOpenFile(@"C:\Downloads\bad.json");
        rig.Backend.FailNext<IGamesApi>(nameof(IGamesApi.ImportDefinitionAsync), ErrorInfo.From(new ValidationException([new ValidationIssue("name", ValidationMessageIds.GameNameRequired)])));
        var requests = new List<AddGameRequest>();
        rig.Shell.AddGameRequested += (_, request) => requests.Add(request);

        var pending = ((IAsyncRelayCommand)rig.Shell.FirstRun.StartFromTemplateCommand).ExecuteAsync(null);
        ((TemplatePickerViewModel)rig.Dialogs.Shown[0]).OpenFileCommand.Execute(null);
        await pending;

        Assert.Empty(requests);
        var toast = Assert.Single(rig.Toasts.Toasts);
        Assert.Equal(ToastKind.Error, toast.Message.Kind);
        Assert.Equal("Some of these fields aren't valid yet. Check the name and paths above.", toast.Text);
    }

    // ---- Jobs

    [Fact]
    public async Task The_shell_shows_a_running_job_in_its_pill_and_a_toast_when_it_finishes()
    {
        var rig = await ShellRig.StartAsync();

        var job = FakeAssert.Ok(await rig.Backend.Profiles.CaptureAsync(SampleScenario.SkyrimId, SampleScenario.ModdedId, "A point"));

        Assert.True(rig.Shell.JobStatus.IsVisible);
        Assert.Equal("Taking a snapshot of Skyrim Special Edition\u2026", rig.Shell.JobStatus.PillText);

        rig.Backend.Jobs.Complete(job.JobId);

        Assert.False(rig.Shell.JobStatus.IsVisible);
        Assert.Equal("Took a snapshot of Skyrim Special Edition.", Assert.Single(rig.Toasts.Toasts).Text);
    }

    [Fact]
    public async Task Pressing_the_pill_opens_activity_on_the_running_jobs_game()
    {
        var rig = await ShellRig.StartAsync();
        var requests = new List<ActivityRequest>();
        rig.Shell.ActivityRequested += (_, request) => requests.Add(request);
        FakeAssert.Ok(await rig.Backend.Games.OptimizeStorageAsync(SampleScenario.SatisfactoryId));

        rig.Shell.JobStatus.OpenCommand.Execute(null);

        Assert.Equal(NavDestination.Activity, rig.Shell.Destination);
        Assert.Equal(SampleScenario.SatisfactoryId, rig.Shell.SelectedGame?.Id);
        Assert.Equal([new ActivityRequest(SampleScenario.SatisfactoryId)], requests);
    }

    [Fact]
    public async Task Pressing_the_pill_for_a_job_across_every_game_opens_activity_on_all_games_and_keeps_the_selected_game()
    {
        var rig = await ShellRig.StartAsync();
        var requests = new List<ActivityRequest>();
        rig.Shell.ActivityRequested += (_, request) => requests.Add(request);
        FakeAssert.Ok(await rig.Backend.Settings.OptimizeAllStorageAsync());

        rig.Shell.JobStatus.OpenCommand.Execute(null);

        Assert.Equal(NavDestination.Activity, rig.Shell.Destination);
        Assert.Equal(SampleScenario.SkyrimId, rig.Shell.SelectedGame?.Id);
        Assert.Equal([new ActivityRequest(null)], requests);
    }

    [Fact]
    public async Task Jobs_already_running_when_the_shell_starts_show_in_the_pill()
    {
        var backend = FakeBackend.Sample();
        FakeAssert.Ok(await backend.Settings.RelocateStorageAsync(@"E:\SavePoint"));

        var rig = await ShellRig.StartAsync(backend);

        Assert.True(rig.Shell.JobStatus.IsVisible);
        Assert.Equal("Moving your snapshots\u2026", rig.Shell.JobStatus.PillText);
    }

    [Fact]
    public async Task A_game_job_already_running_when_the_shell_starts_names_its_game_in_the_pill()
    {
        var backend = FakeBackend.Sample();
        FakeAssert.Ok(await backend.Profiles.CaptureAsync(SampleScenario.SkyrimId, SampleScenario.ModdedId, "A point"));

        var rig = await ShellRig.StartAsync(backend);

        Assert.Equal("Taking a snapshot of Skyrim Special Edition…", rig.Shell.JobStatus.PillText);
    }

    [Fact]
    public async Task The_pill_catches_up_when_a_reload_changes_a_games_name()
    {
        var rig = await ShellRig.StartAsync();
        FakeAssert.Ok(await rig.Backend.Games.OptimizeStorageAsync(SampleScenario.SatisfactoryId));
        Assert.Equal("Freeing up space for Satisfactory…", rig.Shell.JobStatus.PillText);

        FakeAssert.Ok(await rig.Backend.Games.UpdateAsync(SampleScenario.Satisfactory with { Name = "Satisfactory (modded)" }));
        await rig.Shell.RefreshLibraryAsync();

        Assert.Equal("Freeing up space for Satisfactory (modded)…", rig.Shell.JobStatus.PillText);
    }

    [Fact]
    public async Task Job_events_are_handed_to_the_ui_dispatcher()
    {
        var dispatcher = new QueueingDispatcher();
        var rig = await ShellRig.StartAsync(dispatcher: dispatcher);
        dispatcher.RunAll();

        var job = FakeAssert.Ok(await rig.Backend.Profiles.CaptureAsync(SampleScenario.SkyrimId, SampleScenario.ModdedId, "A point"));

        Assert.False(rig.Shell.JobStatus.IsVisible);
        Assert.True(dispatcher.Pending > 0);

        dispatcher.RunAll();

        Assert.True(rig.Shell.JobStatus.IsVisible);
        rig.Backend.Jobs.Complete(job.JobId);
        dispatcher.RunAll();
        Assert.False(rig.Shell.JobStatus.IsVisible);
    }

    [Fact]
    public async Task A_disposed_shell_stops_following_jobs()
    {
        var rig = await ShellRig.StartAsync();
        rig.Shell.Dispose();

        FakeAssert.Ok(await rig.Backend.Profiles.CaptureAsync(SampleScenario.SkyrimId, SampleScenario.ModdedId, "A point"));

        Assert.False(rig.Shell.JobStatus.IsVisible);
    }

    [Fact]
    public async Task The_job_pill_uses_the_names_of_the_games_in_the_library()
    {
        var rig = await ShellRig.StartAsync();

        FakeAssert.Ok(await rig.Backend.Games.OptimizeStorageAsync(SampleScenario.CaretakerId));

        Assert.Equal("Freeing up space for The Last Caretaker\u2026", rig.Shell.JobStatus.PillText);
    }

    // ---- The launch flow slot

    [Fact]
    public async Task A_launch_flow_given_to_the_shell_is_what_screens_see()
    {
        var flow = new FakeLaunchFlow();

        var rig = await ShellRig.StartAsync(launchFlow: flow);

        Assert.Same(flow, ((IShellContext)rig.Shell).LaunchFlow);
        Assert.Same(flow, rig.Shell.LaunchFlow);
    }
}
