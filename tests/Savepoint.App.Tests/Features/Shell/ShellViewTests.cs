using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Savepoint.App.Features.Common.Art;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Theme;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.Shell;

public class ShellViewTests
{
    private static FakeBackend Empty() => new(ScenarioBuilder.Empty().WithSettings(AppSettings.Defaults(@"D:\SavePoint")));

    private static IEnumerable<string?> VisibleTexts(ShellViewHarness harness) =>
        harness.All<TextBlock>().Where(block => block.IsEffectivelyVisible && block.Opacity > 0).Select(block => block.Text);

    // ---- The sidebar layout (3a)

    [AvaloniaFact]
    public async Task The_sidebar_layout_shows_the_rail_and_hides_the_top_bar_and_shelf()
    {
        using var harness = await ShellViewHarness.StartAsync();

        Assert.True(harness.Find<Border>("Rail").IsEffectivelyVisible);
        Assert.False(harness.Find<Border>("TopBar").IsEffectivelyVisible);
        Assert.False(harness.Find<Border>("Shelf").IsEffectivelyVisible);
        Assert.Equal(236, harness.Find<Border>("Rail").Bounds.Width);
    }

    [AvaloniaFact]
    public async Task The_rail_lists_the_destinations_and_the_games_with_their_counts()
    {
        using var harness = await ShellViewHarness.StartAsync();

        var texts = VisibleTexts(harness).ToList();

        Assert.Contains("SavePoint", texts);
        Assert.Contains("Games", texts);
        Assert.Contains("History", texts);
        Assert.Contains("Settings", texts);
        Assert.Contains("YOUR GAMES", texts);
        Assert.Contains("Skyrim Special Edition", texts);
        Assert.Contains("3 profiles", texts);
        Assert.Contains("1 profile", texts);
        Assert.Contains("Add a game", texts);
        Assert.Equal(3, harness.Find<ListBox>("RailGames").ItemCount);
    }

    [AvaloniaFact]
    public async Task The_rail_marks_the_current_destination_and_the_selected_game()
    {
        using var harness = await ShellViewHarness.StartAsync();

        Assert.Equal(0, harness.Find<ListBox>("NavList").SelectedIndex);
        Assert.Equal(0, harness.Find<ListBox>("RailGames").SelectedIndex);

        harness.Shell.Navigate(NavDestination.History);
        harness.Shell.SelectGame(MockupScenario.CaretakerId);
        harness.Settle();

        Assert.Equal(1, harness.Find<ListBox>("NavList").SelectedIndex);
        Assert.Equal(2, harness.Find<ListBox>("RailGames").SelectedIndex);
    }

    [AvaloniaFact]
    public async Task Choosing_in_the_lists_drives_the_view_model()
    {
        using var harness = await ShellViewHarness.StartAsync();

        harness.Find<ListBox>("RailGames").SelectedIndex = 1;
        harness.Find<ListBox>("NavList").SelectedIndex = 2;
        harness.Settle();

        Assert.Equal(NavDestination.Settings, harness.Shell.Destination);
        Assert.Equal(MockupScenario.SatisfactoryId, harness.Shell.SelectedGame?.Id);
    }

    [AvaloniaFact]
    public async Task The_selected_game_stays_selected_in_the_rail_and_on_the_shelf_when_the_library_reloads()
    {
        using var rail = await ShellViewHarness.StartAsync();
        using var shelf = await ShellViewHarness.LibraryLayoutAsync();
        rail.Shell.SelectGame(MockupScenario.SatisfactoryId);
        shelf.Shell.SelectGame(MockupScenario.CaretakerId);

        await rail.Shell.RefreshLibraryAsync();
        await shelf.Shell.RefreshLibraryAsync();
        rail.Settle();
        shelf.Settle();

        Assert.Equal(1, rail.Find<ListBox>("RailGames").SelectedIndex);
        Assert.Equal(2, shelf.Find<ListBox>("ShelfGames").SelectedIndex);
        Assert.Equal(MockupScenario.SatisfactoryId, rail.Shell.SelectedGame?.Id);
    }

    [AvaloniaFact]
    public async Task The_add_button_is_outlined_while_there_are_games_and_asks_for_the_editor()
    {
        using var harness = await ShellViewHarness.StartAsync();
        var requested = 0;
        harness.Shell.AddGameRequested += (_, _) => requested++;
        var add = harness.Find<Button>("RailAddButton");

        Assert.DoesNotContain("filled", add.Classes);

        add.Command!.Execute(null);

        Assert.Equal(1, requested);
    }

    [AvaloniaFact]
    public async Task Each_game_gets_a_tile_tinted_from_its_name()
    {
        using var harness = await ShellViewHarness.StartAsync();

        var tiles = harness.Find<ListBox>("RailGames").GetVisualDescendants().OfType<ArtTile>().ToList();

        Assert.Equal(["Skyrim Special Edition", "Satisfactory", "The Last Caretaker"], tiles.Select(tile => tile.GameName));
        Assert.All(tiles, tile => Assert.Equal(24, tile.Width));
    }

    [AvaloniaFact]
    public async Task Nothing_in_the_shell_is_an_unnamed_icon_button()
    {
        using var harness = await ShellViewHarness.StartAsync();

        Assert.Empty(AutomationAudit.IconButtonsWithoutNames(harness.View));
    }

    [AvaloniaFact]
    public async Task Navigation_and_game_items_have_automation_names()
    {
        using var harness = await ShellViewHarness.StartAsync();

        var nav = harness.Find<ListBox>("NavList").GetVisualDescendants().OfType<ListBoxItem>().ToList();
        var games = harness.Find<ListBox>("RailGames").GetVisualDescendants().OfType<ListBoxItem>().ToList();

        Assert.Equal(["Games", "History", "Settings"], nav.Select(AutomationProperties.GetName));
        Assert.Equal("Skyrim Special Edition, 3 profiles", AutomationProperties.GetName(games[0]));
        Assert.Equal("Main", AutomationProperties.GetName(harness.Find<ListBox>("NavList")));
        Assert.Equal(AutomationLandmarkType.Navigation, AutomationProperties.GetLandmarkType(harness.Find<Border>("Rail")));
    }

    // ---- The library layout (3b)

    [AvaloniaFact]
    public async Task The_library_layout_shows_the_top_bar_and_the_shelf_and_hides_the_rail()
    {
        using var harness = await ShellViewHarness.LibraryLayoutAsync();

        Assert.False(harness.Find<Border>("Rail").IsEffectivelyVisible);
        Assert.True(harness.Find<Border>("TopBar").IsEffectivelyVisible);
        Assert.True(harness.Find<Border>("Shelf").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task The_shelf_has_a_card_per_game_with_count_and_size_and_a_dashed_add_tile()
    {
        using var harness = await ShellViewHarness.LibraryLayoutAsync();

        var texts = VisibleTexts(harness).ToList();

        Assert.Equal(3, harness.Find<ListBox>("ShelfGames").ItemCount);
        Assert.Contains("3 profiles \u00b7 41.2 GB", texts);
        Assert.Contains("2 profiles \u00b7 28.7 GB", texts);
        Assert.Contains("1 profile \u00b7 9.4 GB", texts);
        var tile = harness.Find<Button>("ShelfAddTile");
        Assert.Contains("dashed", tile.Classes);
        Assert.Equal("Add a game", AutomationProperties.GetName(tile));
    }

    [AvaloniaFact]
    public async Task Choosing_a_card_selects_the_game()
    {
        using var harness = await ShellViewHarness.LibraryLayoutAsync();

        harness.Find<ListBox>("ShelfGames").SelectedIndex = 1;
        harness.Settle();

        Assert.Equal(MockupScenario.SatisfactoryId, harness.Shell.SelectedGame?.Id);
    }

    [AvaloniaFact]
    public async Task The_top_bar_navigation_drives_the_destination()
    {
        using var harness = await ShellViewHarness.LibraryLayoutAsync();

        harness.Find<ListBox>("TopNavList").SelectedIndex = 1;
        harness.Settle();

        Assert.Equal(NavDestination.History, harness.Shell.Destination);
        Assert.False(harness.Find<Border>("Shelf").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task The_top_bar_shows_the_layout_toggle_with_the_current_layout_selected()
    {
        using var harness = await ShellViewHarness.LibraryLayoutAsync();

        var toggle = harness.All<LayoutToggleView>().Single(view => view.IsEffectivelyVisible);
        var list = (ListBox)toggle.FindControl<ListBox>("Toggle")!;

        Assert.Equal(1, list.SelectedIndex);
        Assert.Equal("Layout", AutomationProperties.GetName(list));
        Assert.Equal(["Sidebar", "Library"], list.GetVisualDescendants().OfType<ListBoxItem>().Select(AutomationProperties.GetName));
    }

    // ---- Changing layout keeps what is on screen

    [AvaloniaFact]
    public async Task Switching_layout_with_the_toggle_swaps_the_chrome_but_keeps_the_same_screen_instance()
    {
        using var harness = await ShellViewHarness.LibraryLayoutAsync();
        var before = harness.Find<ContentControl>("ContentRegion").Content;
        var toggle = harness.All<LayoutToggleView>().Single(view => view.IsEffectivelyVisible).FindControl<ListBox>("Toggle")!;

        toggle.SelectedIndex = 0;
        harness.Settle();

        Assert.Equal(ShellLayout.Sidebar, harness.Shell.Layout);
        Assert.True(harness.Find<Border>("Rail").IsEffectivelyVisible);
        Assert.False(harness.Find<Border>("TopBar").IsEffectivelyVisible);
        Assert.Same(before, harness.Find<ContentControl>("ContentRegion").Content);
    }

    // ---- Responsive (below 900px)

    [AvaloniaFact]
    public async Task Narrowing_the_window_collapses_the_rail_to_icons_and_widening_brings_the_names_back()
    {
        using var harness = await ShellViewHarness.StartAsync();

        harness.Window.Width = 760;
        harness.Settle();

        Assert.True(harness.Shell.IsCompact);
        Assert.Equal(64, harness.Find<Border>("Rail").Bounds.Width);

        // The rail only: the Games screen beside it names the selected game in its own header.
        var texts = harness.Find<Border>("Rail").GetVisualDescendants().OfType<TextBlock>()
            .Where(block => block.IsEffectivelyVisible && block.Opacity > 0).Select(block => block.Text).ToList();
        Assert.DoesNotContain("Games", texts);
        Assert.DoesNotContain("Skyrim Special Edition", texts);
        Assert.DoesNotContain("Add a game", texts);

        harness.Window.Width = 1040;
        harness.Settle();

        Assert.False(harness.Shell.IsCompact);
        Assert.Equal(236, harness.Find<Border>("Rail").Bounds.Width);
        Assert.Contains("Games", VisibleTexts(harness));
    }

    [AvaloniaFact]
    public async Task A_collapsed_rail_keeps_names_as_tooltips_and_automation_names()
    {
        using var harness = await ShellViewHarness.StartAsync();

        harness.Window.Width = 760;
        harness.Settle();

        var nav = harness.Find<ListBox>("NavList").GetVisualDescendants().OfType<ListBoxItem>().ToList();
        var games = harness.Find<ListBox>("RailGames").GetVisualDescendants().OfType<ListBoxItem>().ToList();
        Assert.Equal(["Games", "History", "Settings"], nav.Select(ToolTip.GetTip));
        Assert.Equal(["Games", "History", "Settings"], nav.Select(AutomationProperties.GetName));
        Assert.Equal("Skyrim Special Edition, 3 profiles", ToolTip.GetTip(games[0]));
        Assert.Equal("Skyrim Special Edition, 3 profiles", AutomationProperties.GetName(games[0]));
        var add = harness.Find<Button>("RailAddButton");
        Assert.Equal("Add a game", AutomationProperties.GetName(add));
        Assert.Equal("Add a game", ToolTip.GetTip(add));
        Assert.Empty(AutomationAudit.IconButtonsWithoutNames(harness.View));
    }

    [AvaloniaFact]
    public async Task A_short_window_drops_the_art_from_the_library_shelf_to_leave_room_for_the_page()
    {
        using var harness = await ShellViewHarness.LibraryLayoutAsync(height: 432);

        var art = harness.Find<ListBox>("ShelfGames").GetVisualDescendants().OfType<ArtTile>().ToList();
        Assert.NotEmpty(art);
        Assert.All(art, tile => Assert.False(tile.IsEffectivelyVisible));

        harness.Window.Height = 660;
        harness.Settle();

        Assert.All(harness.Find<ListBox>("ShelfGames").GetVisualDescendants().OfType<ArtTile>(), tile => Assert.True(tile.IsEffectivelyVisible));
    }

    [AvaloniaFact]
    public async Task A_long_list_of_games_scrolls_inside_the_rail_and_leaves_Add_a_game_in_view()
    {
        var scenario = ScenarioBuilder.Mockup();
        foreach (var number in Enumerable.Range(1, 14))
        {
            scenario = scenario.WithGame(MockupScenario.Satisfactory with { Id = $"extra-game-{number}", Name = $"Extra game {number}" });
        }

        using var harness = await ShellViewHarness.StartAsync(new FakeBackend(scenario), height: 432);

        var problems = LayoutAudit.Problems(harness.Window);
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        Assert.True(LayoutAudit.BoundsIn(harness.Window, harness.Find<Button>("RailAddButton")).Bottom <= 432);
    }

    [AvaloniaFact]
    public async Task A_collapsed_rail_tells_games_apart_by_their_initials_and_not_by_tint_alone()
    {
        using var harness = await ShellViewHarness.StartAsync();

        harness.Window.Width = 760;
        harness.Settle();

        var tiles = harness.Find<ListBox>("RailGames").GetVisualDescendants().OfType<ArtTile>().ToList();
        Assert.Equal(["SS", "Sa", "LC"], tiles.Select(tile => tile.Initials));

        harness.Window.Width = 1040;
        harness.Settle();

        Assert.All(harness.Find<ListBox>("RailGames").GetVisualDescendants().OfType<ArtTile>(), tile => Assert.Equal(string.Empty, tile.Initials));
    }

    [AvaloniaFact]
    public async Task Keyboard_focus_on_a_game_in_a_collapsed_rail_shows_its_name_as_the_tooltip_does_for_the_pointer()
    {
        using var harness = await ShellViewHarness.StartAsync();
        harness.Window.Width = 760;
        harness.Settle();
        var game = harness.Find<ListBox>("RailGames").GetVisualDescendants().OfType<ListBoxItem>().First();

        game.Focus(NavigationMethod.Tab);
        harness.Settle();
        Assert.True(ToolTip.GetIsOpen(game));

        harness.Find<Button>("RailAddButton").Focus(NavigationMethod.Tab);
        harness.Settle();
        Assert.False(ToolTip.GetIsOpen(game));
    }

    [AvaloniaFact]
    public async Task An_expanded_rail_shows_no_tooltips_for_names_already_on_screen()
    {
        using var harness = await ShellViewHarness.StartAsync();

        var nav = harness.Find<ListBox>("NavList").GetVisualDescendants().OfType<ListBoxItem>().ToList();

        Assert.All(nav, item => Assert.Null(ToolTip.GetTip(item)));
    }

    // ---- First run (2e)

    [AvaloniaFact]
    public async Task An_empty_library_shows_the_first_run_state_in_the_content_region()
    {
        using var harness = await ShellViewHarness.StartAsync(Empty());

        Assert.Single(harness.All<FirstRunView>());
        var texts = VisibleTexts(harness).ToList();
        Assert.Contains("Keep the setup that works", texts);
        Assert.Contains("Add your first game", texts);
        Assert.Contains("Start from a template", texts);
        Assert.Contains("No games yet.", texts);
    }

    private const string _artworkNote = "SavePoint downloads cover art for Steam games from Steam. You can turn this off in Settings.";

    [AvaloniaFact]
    public async Task First_run_says_before_anything_happens_that_cover_art_is_downloaded_from_Steam()
    {
        using var harness = await ShellViewHarness.StartAsync(Empty());

        Assert.Contains(_artworkNote, VisibleTexts(harness));
        Assert.True(harness.Find<StackPanel>("ArtworkNote").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task First_run_stays_quiet_about_cover_art_when_the_lookup_is_already_off()
    {
        var backend = new FakeBackend(ScenarioBuilder.Empty().WithSettings(AppSettings.Defaults(@"D:\SavePoint") with { LookUpArtworkOnline = false }));
        using var harness = await ShellViewHarness.StartAsync(backend);

        Assert.DoesNotContain(_artworkNote, VisibleTexts(harness));
    }

    [AvaloniaFact]
    public async Task First_run_says_where_data_is_kept_and_that_kernel_anti_cheat_is_not_supported()
    {
        using var harness = await ShellViewHarness.StartAsync(Empty());

        var note = harness.Find<Border>("StorageNote");
        var runs = note.GetVisualDescendants().OfType<TextBlock>().SelectMany(block => block.Inlines ?? []).OfType<Avalonia.Controls.Documents.Run>().Select(run => run.Text).ToList();

        Assert.True(note.IsEffectivelyVisible);
        Assert.Equal(["SavePoint keeps your snapshots in ", @"D:\SavePoint", " \u2014 you can change that in Settings."], runs);
        Assert.True(harness.Find<StackPanel>("AntiCheatNote").IsEffectivelyVisible);
        Assert.Contains("Games protected by kernel-level anti-cheat aren't supported.", VisibleTexts(harness));
    }

    [AvaloniaFact]
    public async Task In_first_run_the_rail_button_is_filled_and_history_is_dimmed()
    {
        using var harness = await ShellViewHarness.StartAsync(Empty());

        Assert.Contains("filled", harness.Find<Button>("RailAddButton").Classes);
        var items = harness.Find<ListBox>("NavList").GetVisualDescendants().OfType<ListBoxItem>().ToList();
        Assert.Equal([true, false, true], items.Select(item => item.IsEnabled));
        var dimmed = (Avalonia.Media.ISolidColorBrush)Application.Current!.FindResource("Md3DisabledContentBrush")!;
        Assert.Equal(dimmed.Color, ((Avalonia.Media.ISolidColorBrush)items[1].Foreground!).Color);
        Assert.NotEqual(dimmed.Color, ((Avalonia.Media.ISolidColorBrush)items[2].Foreground!).Color);
    }

    [AvaloniaFact]
    public async Task The_first_run_buttons_ask_for_the_editor_and_the_template_picker()
    {
        using var harness = await ShellViewHarness.StartAsync(Empty(), realDialogs: true);
        var requests = 0;
        harness.Shell.AddGameRequested += (_, _) => requests++;

        // The template button goes first: "Add your first game" opens the editor pane, which takes the first-run view out of the tree.
        harness.Find<Button>("StartFromTemplateButton").Command!.Execute(null);
        harness.Find<Button>("AddFirstGameButton").Command!.Execute(null);
        harness.Settle();

        Assert.Equal(1, requests);
        Assert.NotNull(harness.Main.Dialogs.Current);
        Assert.IsType<TemplatePickerViewModel>(harness.Main.Dialogs.Current);
    }

    [AvaloniaFact]
    public async Task The_template_picker_dialog_lists_the_built_in_templates()
    {
        using var harness = await ShellViewHarness.StartAsync(Empty());
        _ = harness.Main.Dialogs.ShowAsync(new TemplatePickerViewModel(harness.Backend.Games.Templates, TextFor(harness)));
        harness.Settle();

        var picker = harness.All<TemplatePickerView>().Single();
        var texts = picker.GetVisualDescendants().OfType<TextBlock>().Where(block => block.IsEffectivelyVisible).Select(block => block.Text).ToList();

        Assert.Contains("Start from a template", texts);
        Assert.Contains("The Elder Scrolls V: Skyrim Special Edition", texts);
        Assert.Contains("A Simple Indie Platformer", texts);
    }

    // ---- The overlays

    [AvaloniaFact]
    public async Task The_job_pill_shows_in_both_layouts_while_a_job_runs()
    {
        using var sidebar = await ShellViewHarness.StartAsync();
        using var library = await ShellViewHarness.LibraryLayoutAsync();
        Assert.False(sidebar.Find<UserControl>("JobPill").FindControl<Grid>("PillRoot")!.IsEffectivelyVisible);

        FakeAssert.Ok(await sidebar.Backend.Profiles.CaptureAsync(MockupScenario.SkyrimId, MockupScenario.ModdedId, "A point"));
        FakeAssert.Ok(await library.Backend.Profiles.CaptureAsync(MockupScenario.SkyrimId, MockupScenario.ModdedId, "A point"));
        sidebar.Settle();
        library.Settle();

        Assert.Contains("Saving Skyrim Special Edition\u2026", VisibleTexts(sidebar));
        Assert.Contains("Saving Skyrim Special Edition\u2026", VisibleTexts(library));
    }

    [AvaloniaFact]
    public async Task The_job_pill_is_gone_when_the_job_ends_and_a_toast_says_how_it_went()
    {
        using var harness = await ShellViewHarness.StartAsync();
        var job = FakeAssert.Ok(await harness.Backend.Profiles.CaptureAsync(MockupScenario.SkyrimId, MockupScenario.ModdedId, "A point"));

        harness.Backend.Jobs.Complete(job.JobId);
        harness.Settle();

        var texts = VisibleTexts(harness).ToList();
        Assert.DoesNotContain("Saving Skyrim Special Edition\u2026", texts);
        Assert.Contains("Saved Skyrim Special Edition.", texts);
    }

    [AvaloniaFact]
    public async Task Toasts_sit_in_the_bottom_right_corner()
    {
        using var harness = await ShellViewHarness.StartAsync();

        harness.Main.Toasts.Show(new ToastMessage("Storage moved.", ToastKind.Success));
        harness.Settle();

        var host = harness.All<ToastHostView>().Single();
        var corner = host.TranslatePoint(new Point(host.Bounds.Width, host.Bounds.Height), harness.View)!.Value;
        Assert.InRange(corner.X, harness.View.Bounds.Width - 20, harness.View.Bounds.Width);
        Assert.InRange(corner.Y, harness.View.Bounds.Height - 40, harness.View.Bounds.Height);
    }

    [AvaloniaFact]
    public async Task While_a_dialog_is_open_the_page_is_hidden_from_assistive_technology()
    {
        using var harness = await ShellViewHarness.StartAsync();
        var page = harness.Find<ContentControl>("Page");
        Assert.NotEqual(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(page));

        var pending = harness.Main.Dialogs.ShowAsync(new Common.Samples.SampleDialogViewModel());
        harness.Settle();

        Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(page));

        harness.Main.Dialogs.RequestDismissCurrent();
        harness.Settle();

        Assert.True(pending.IsCompleted);
        Assert.NotEqual(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(page));
    }

    // ---- Startup screens

    [AvaloniaFact]
    public async Task The_startup_screen_names_a_missing_tool_and_offers_to_try_again()
    {
        var path = @"C:\Program Files\SavePoint\git\cmd\git.exe";
        var status = new StartupStatus(
            new GitStatus(false, path, null, ErrorInfo.From(new GitUnavailableException(path, "dev text", null))),
            new CatalogStatus(@"C:\Users\you\AppData\Roaming\SavePoint", []));
        using var harness = await ShellViewHarness.StartAsync(new FakeBackend(ScenarioBuilder.Mockup().WithStartupStatus(status)));

        var texts = VisibleTexts(harness).ToList();

        Assert.Single(harness.All<StartupView>());
        Assert.Contains("SavePoint's built-in tool is missing", texts);
        Assert.Contains(path, harness.All<SelectableTextBlock>().Select(block => block.Text));
        Assert.Contains("Try again", harness.All<Button>().Select(button => button.Content));
        Assert.Empty(harness.All<ShellView>());
        Assert.False(harness.Find<UserControl>("JobPill").FindControl<Grid>("PillRoot")!.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task The_recovery_screen_lists_each_file_and_disables_restore_when_there_is_no_backup()
    {
        var directory = @"C:\Users\you\AppData\Roaming\SavePoint";
        var games = directory + @"\games.json";
        var profiles = directory + @"\profiles.json";
        var status = new StartupStatus(
            new GitStatus(true, @"C:\git.exe", "2.56.0", null),
            new CatalogStatus(directory, [new CorruptCatalogFile(games, games + ".bak", ErrorInfo.From(new CatalogCorruptException(games, null))), new CorruptCatalogFile(profiles, null, ErrorInfo.From(new CatalogCorruptException(profiles, null)))]));
        using var harness = await ShellViewHarness.StartAsync(new FakeBackend(ScenarioBuilder.Mockup().WithStartupStatus(status)));

        var restore = harness.All<Button>().Where(button => "Restore the backup".Equals(button.Content)).ToList();

        Assert.Equal(2, restore.Count);
        Assert.True(restore[0].IsEffectivelyEnabled);
        Assert.False(restore[1].IsEffectivelyEnabled);
        Assert.Contains(games, harness.All<SelectableTextBlock>().Select(block => block.Text));
        Assert.Contains("Start with nothing in it", harness.All<Button>().Select(button => button.Content));
    }

    [AvaloniaFact]
    public async Task Recovering_the_last_file_brings_the_shell_up()
    {
        var directory = @"C:\Users\you\AppData\Roaming\SavePoint";
        var games = directory + @"\games.json";
        var status = new StartupStatus(
            new GitStatus(true, @"C:\git.exe", "2.56.0", null),
            new CatalogStatus(directory, [new CorruptCatalogFile(games, games + ".bak", ErrorInfo.From(new CatalogCorruptException(games, null)))]));
        using var harness = await ShellViewHarness.StartAsync(new FakeBackend(ScenarioBuilder.Mockup().WithStartupStatus(status)));

        harness.All<Button>().First(button => "Restore the backup".Equals(button.Content)).Command!.Execute(null);
        harness.Settle();
        await Task.Yield();
        harness.Settle();

        Assert.NotNull(harness.Main.Shell);
        Assert.Single(harness.All<ShellView>());
    }

    // ---- The window

    [AvaloniaFact]
    public void The_main_window_hosts_the_main_view_and_keeps_its_default_size()
    {
        var window = new Savepoint.App.Views.MainWindow();

        Assert.IsType<MainView>(window.Content);
        Assert.Equal(1040, window.Width);
    }

    private static Savepoint.App.Features.Common.Formatting.UiFormatter TextFor(ShellViewHarness harness) =>
        new(Savepoint.App.Localization.LocalizationCatalog.Current, harness.Backend.Clock, "windows", TimeZoneInfo.Utc);
}
