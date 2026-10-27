using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Savepoint.App.Features.Common.Art;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Features.Help;
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

    // ---- The sidebar layout

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
        harness.Shell.SelectGame(SampleScenario.CaretakerId);
        harness.Settle();

        Assert.Equal(1, harness.Find<ListBox>("NavList").SelectedIndex);
        Assert.Equal(2, harness.Find<ListBox>("RailGames").SelectedIndex);
    }

    [AvaloniaFact]
    public async Task Choosing_in_the_lists_drives_the_view_model()
    {
        using var harness = await ShellViewHarness.StartAsync();

        harness.Find<ListBox>("RailGames").SelectedIndex = 1;
        harness.Find<ListBox>("NavList").SelectedIndex = 3;
        harness.Settle();

        Assert.Equal(NavDestination.Settings, harness.Shell.Destination);
        Assert.Equal(SampleScenario.SatisfactoryId, harness.Shell.SelectedGame?.Id);
    }

    private static void ClickCenter(ShellViewHarness harness, Control control)
    {
        var bounds = LayoutAudit.BoundsIn(harness.Window, control);
        var point = bounds.Center;
        harness.Window.MouseDown(point, MouseButton.Left);
        harness.Window.MouseUp(point, MouseButton.Left);
        harness.Settle();
    }

    private static ListBoxItem ItemOf(ShellViewHarness harness, string list, int index) =>
        harness.Find<ListBox>(list).GetVisualDescendants().OfType<ListBoxItem>().ElementAt(index);

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Clicking_a_game_in_the_rail_opens_it_from_settings_even_when_it_is_already_selected(int index)
    {
        using var harness = await ShellViewHarness.StartAsync();
        harness.Shell.Navigate(NavDestination.Settings);
        harness.Settle();

        ClickCenter(harness, ItemOf(harness, "RailGames", index));

        Assert.Equal(NavDestination.Games, harness.Shell.Destination);
        Assert.Equal(harness.Shell.Games[index].Id, harness.Shell.SelectedGame?.Id);
    }

    [AvaloniaFact]
    public async Task Clicking_a_game_in_the_rail_closes_the_help_page()
    {
        using var harness = await ShellViewHarness.StartAsync();
        harness.Shell.ShowHelp();
        harness.Settle();

        ClickCenter(harness, ItemOf(harness, "RailGames", 0));

        Assert.IsNotType<HowItWorksViewModel>(harness.Shell.Content);
    }

    [AvaloniaFact]
    public async Task Arrowing_through_the_rail_keeps_settings_open_and_enter_opens_the_game()
    {
        using var harness = await ShellViewHarness.StartAsync();
        harness.Shell.Navigate(NavDestination.Settings);
        harness.Settle();
        var first = ItemOf(harness, "RailGames", 0);
        first.Focus();
        harness.Settle();

        harness.Window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
        harness.Window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
        harness.Settle();

        Assert.Equal(NavDestination.Settings, harness.Shell.Destination);
        Assert.Equal(SampleScenario.SatisfactoryId, harness.Shell.SelectedGame?.Id);

        harness.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        harness.Window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        harness.Settle();

        Assert.Equal(NavDestination.Games, harness.Shell.Destination);
    }

    [AvaloniaTheory]
    [InlineData("RailLibrariesButton")]
    [InlineData("RailAddButton")]
    public async Task A_rail_button_label_keeps_clear_of_the_button_edge_and_is_not_cut_off(string button)
    {
        using var harness = await ShellViewHarness.StartAsync();
        var target = harness.Find<Button>(button);
        var label = target.GetVisualDescendants().OfType<TextBlock>().First(block => !block.Classes.Contains("icon") && block.IsEffectivelyVisible);

        var bounds = LayoutAudit.BoundsIn(harness.Window, target);
        var text = LayoutAudit.BoundsIn(harness.Window, label);
        var lines = label.TextLayout.TextLines;

        Assert.True(text.Left - bounds.Left >= 16, $"The label starts {text.Left - bounds.Left:0}px from the button's left edge.");
        Assert.True(bounds.Right - text.Right >= 16, $"The label ends {bounds.Right - text.Right:0}px from the button's right edge.");
        Assert.DoesNotContain(lines, line => line.HasCollapsed);
    }

    [AvaloniaTheory]
    [InlineData("RailLibrariesButton")]
    [InlineData("RailAddButton")]
    public async Task A_rail_button_label_wraps_so_a_longer_translation_cannot_overflow(string button)
    {
        using var harness = await ShellViewHarness.StartAsync();
        var label = harness.Find<Button>(button).GetVisualDescendants().OfType<TextBlock>().First(block => !block.Classes.Contains("icon") && block.IsEffectivelyVisible);

        Assert.Equal(TextWrapping.Wrap, label.TextWrapping);
    }

    [AvaloniaFact]
    public async Task In_the_icon_rail_both_buttons_centre_their_icon()
    {
        using var harness = await ShellViewHarness.StartAsync(width: 800);

        double Offset(string name)
        {
            var target = harness.Find<Button>(name);
            var icon = target.GetVisualDescendants().OfType<TextBlock>().First(block => block.Classes.Contains("icon"));
            return LayoutAudit.BoundsIn(harness.Window, icon).Center.X - LayoutAudit.BoundsIn(harness.Window, target).Center.X;
        }

        Assert.True(harness.Shell.IsCompact);
        Assert.InRange(Offset("RailAddButton"), -1, 1);
        Assert.InRange(Offset("RailLibrariesButton"), -1, 1);
    }

    [AvaloniaTheory]
    [InlineData("RailGames")]
    [InlineData("ShelfGames")]
    public async Task Invoking_a_game_through_accessibility_opens_it_from_settings(string list)
    {
        using var harness = list == "RailGames" ? await ShellViewHarness.StartAsync() : await ShellViewHarness.LibraryLayoutAsync();
        var item = ItemOf(harness, list, 1);
        harness.Shell.Navigate(NavDestination.Settings);
        harness.Settle();

        var invoke = Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(item));
        invoke.Invoke();
        harness.Settle();

        Assert.Equal(NavDestination.Games, harness.Shell.Destination);
        Assert.Equal(SampleScenario.SatisfactoryId, harness.Shell.SelectedGame?.Id);
    }

    [AvaloniaFact]
    public async Task Enter_opens_the_focused_game_even_when_it_is_not_the_selected_one()
    {
        using var harness = await ShellViewHarness.StartAsync();
        harness.Shell.Navigate(NavDestination.Settings);
        harness.Settle();
        ItemOf(harness, "RailGames", 0).Focus();
        harness.Settle();

        // Ctrl+arrow moves focus without selecting.
        harness.Window.KeyPress(Key.Down, RawInputModifiers.Control, PhysicalKey.ArrowDown, null);
        harness.Window.KeyRelease(Key.Down, RawInputModifiers.Control, PhysicalKey.ArrowDown, null);
        harness.Settle();
        Assert.Equal(SampleScenario.SkyrimId, harness.Shell.SelectedGame?.Id);

        harness.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        harness.Window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        harness.Settle();

        Assert.Equal(NavDestination.Games, harness.Shell.Destination);
        Assert.Equal(SampleScenario.SatisfactoryId, harness.Shell.SelectedGame?.Id);
    }

    [AvaloniaFact]
    public async Task Clicking_a_card_on_the_shelf_selects_that_game()
    {
        using var harness = await ShellViewHarness.LibraryLayoutAsync();

        ClickCenter(harness, ItemOf(harness, "ShelfGames", 1));

        Assert.Equal(SampleScenario.SatisfactoryId, harness.Shell.SelectedGame?.Id);
        Assert.Equal(NavDestination.Games, harness.Shell.Destination);
    }

    [AvaloniaFact]
    public async Task The_selected_game_stays_selected_in_the_rail_and_on_the_shelf_when_the_library_reloads()
    {
        using var rail = await ShellViewHarness.StartAsync();
        using var shelf = await ShellViewHarness.LibraryLayoutAsync();
        rail.Shell.SelectGame(SampleScenario.SatisfactoryId);
        shelf.Shell.SelectGame(SampleScenario.CaretakerId);

        await rail.Shell.RefreshLibraryAsync();
        await shelf.Shell.RefreshLibraryAsync();
        rail.Settle();
        shelf.Settle();

        Assert.Equal(1, rail.Find<ListBox>("RailGames").SelectedIndex);
        Assert.Equal(2, shelf.Find<ListBox>("ShelfGames").SelectedIndex);
        Assert.Equal(SampleScenario.SatisfactoryId, rail.Shell.SelectedGame?.Id);
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

        Assert.Equal(["Games", "History", "Activity", "Settings"], nav.Select(AutomationProperties.GetName));
        Assert.Equal("Skyrim Special Edition, 3 profiles", AutomationProperties.GetName(games[0]));
        Assert.Equal("Main", AutomationProperties.GetName(harness.Find<ListBox>("NavList")));
        Assert.Equal(AutomationLandmarkType.Navigation, AutomationProperties.GetLandmarkType(harness.Find<Border>("Rail")));
    }

    // ---- The library layout

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

        Assert.Equal(SampleScenario.SatisfactoryId, harness.Shell.SelectedGame?.Id);
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
        Assert.Equal(["Games", "History", "Activity", "Settings"], nav.Select(ToolTip.GetTip));
        Assert.Equal(["Games", "History", "Activity", "Settings"], nav.Select(AutomationProperties.GetName));
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
        var scenario = ScenarioBuilder.Sample();
        foreach (var number in Enumerable.Range(1, 14))
        {
            scenario = scenario.WithGame(SampleScenario.Satisfactory with { Id = $"extra-game-{number}", Name = $"Extra game {number}" });
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

    // ---- First run

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
    public async Task First_run_says_where_data_is_kept_and_warns_against_online_games_with_anti_cheat()
    {
        using var harness = await ShellViewHarness.StartAsync(Empty());

        var note = harness.Find<Border>("StorageNote");
        var runs = note.GetVisualDescendants().OfType<TextBlock>().SelectMany(block => block.Inlines ?? []).OfType<Avalonia.Controls.Documents.Run>().Select(run => run.Text).ToList();

        Assert.True(note.IsEffectivelyVisible);
        Assert.Equal(["SavePoint keeps your snapshots in ", @"D:\SavePoint", ". You can change that in Settings."], runs);
        Assert.True(harness.Find<StackPanel>("AntiCheatNote").IsEffectivelyVisible);
        Assert.Contains("Don't use SavePoint with online games that have anti-cheat. Changed game files can get your account flagged.", VisibleTexts(harness));
    }

    [AvaloniaFact]
    public async Task In_first_run_the_rail_button_is_filled_and_history_is_dimmed()
    {
        using var harness = await ShellViewHarness.StartAsync(Empty());

        Assert.Contains("filled", harness.Find<Button>("RailAddButton").Classes);
        var items = harness.Find<ListBox>("NavList").GetVisualDescendants().OfType<ListBoxItem>().ToList();
        Assert.Equal([true, false, true, true], items.Select(item => item.IsEnabled));
        var dimmed = (Avalonia.Media.ISolidColorBrush)Application.Current!.FindResource("Md3DisabledContentBrush")!;
        Assert.Equal(dimmed.Color, ((Avalonia.Media.ISolidColorBrush)items[1].Foreground!).Color);
        Assert.NotEqual(dimmed.Color, ((Avalonia.Media.ISolidColorBrush)items[2].Foreground!).Color);
        Assert.NotEqual(dimmed.Color, ((Avalonia.Media.ISolidColorBrush)items[3].Foreground!).Color);
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
        var viewModel = new TemplatePickerViewModel(harness.Backend.Games.Templates, TextFor(harness));
        _ = harness.Main.Dialogs.ShowAsync(viewModel);
        harness.Settle();

        var picker = harness.All<TemplatePickerView>().Single();
        List<string?> Texts() => [.. picker.GetVisualDescendants().OfType<TextBlock>().Where(block => block.IsEffectivelyVisible).Select(block => block.Text)];

        Assert.Contains("Start from a template", Texts());
        Assert.NotNull(picker.FindControl<TextBox>("SearchField"));
        Assert.True(picker.FindControl<TextBox>("SearchField")!.IsFocused, "The search field has the keyboard focus as soon as the picker opens.");
        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(picker.FindControl<TextBlock>("NoMatchesText")!));
        Assert.False(picker.FindControl<TextBlock>("NoMatchesText")!.IsEffectivelyVisible);

        // Hundreds of templates ship, so the list scrolls and the search is how a game is found.
        viewModel.SearchText = "skyrim special";
        harness.Settle();
        Assert.Contains("The Elder Scrolls V: Skyrim Special Edition", Texts());
        Assert.DoesNotContain("A Simple Indie Platformer", Texts());

        viewModel.SearchText = "no such game";
        harness.Settle();
        Assert.True(picker.FindControl<TextBlock>("NoMatchesText")!.IsEffectivelyVisible);
    }

    // ---- The overlays

    [AvaloniaFact]
    public async Task The_job_pill_shows_in_both_layouts_while_a_job_runs()
    {
        using var sidebar = await ShellViewHarness.StartAsync();
        using var library = await ShellViewHarness.LibraryLayoutAsync();
        Assert.False(sidebar.Find<UserControl>("JobPill").FindControl<Grid>("PillRoot")!.IsEffectivelyVisible);

        FakeAssert.Ok(await sidebar.Backend.Profiles.CaptureAsync(SampleScenario.SkyrimId, SampleScenario.ModdedId, "A point"));
        FakeAssert.Ok(await library.Backend.Profiles.CaptureAsync(SampleScenario.SkyrimId, SampleScenario.ModdedId, "A point"));
        sidebar.Settle();
        library.Settle();

        Assert.Contains("Taking a snapshot of Skyrim Special Edition\u2026", VisibleTexts(sidebar));
        Assert.Contains("Taking a snapshot of Skyrim Special Edition\u2026", VisibleTexts(library));
    }

    [AvaloniaFact]
    public async Task The_job_pill_is_a_button_named_for_what_it_does_and_opens_activity_from_the_keyboard()
    {
        using var harness = await ShellViewHarness.StartAsync();
        FakeAssert.Ok(await harness.Backend.Games.OptimizeStorageAsync(SampleScenario.SatisfactoryId));
        harness.Settle();
        var pill = Assert.IsType<Button>(harness.Find<UserControl>("JobPill").FindControl<Control>("Pill"));

        Assert.Equal("Freeing up space for Satisfactory… Open Activity", AutomationProperties.GetName(pill));

        pill.Focus(NavigationMethod.Tab);
        harness.Settle();
        harness.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        harness.Window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        harness.Settle();

        Assert.Equal(NavDestination.Activity, harness.Shell.Destination);
        Assert.Equal(SampleScenario.SatisfactoryId, harness.Shell.SelectedGame?.Id);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_job_pill_floats_over_the_shell_and_leaves_its_layout_alone(bool library)
    {
        using var harness = library ? await ShellViewHarness.LibraryLayoutAsync() : await ShellViewHarness.StartAsync();
        var page = harness.Find<ContentControl>("Page");
        var content = harness.Find<ContentControl>("ContentRegion");
        var pill = harness.Find<Button>("Pill");
        var room = harness.All<Border>().First(candidate => candidate.IsEffectivelyVisible && candidate.Classes.Contains("pill-room"));
        var pageBefore = LayoutAudit.BoundsIn(harness.Window, page);
        var contentBefore = LayoutAudit.BoundsIn(harness.Window, content);
        Assert.Equal(0, room.Bounds.Height);
        Assert.False(pill.IsEffectivelyVisible);

        var job = FakeAssert.Ok(await harness.Backend.Games.OptimizeStorageAsync(SampleScenario.SatisfactoryId));
        harness.Settle();

        Assert.True(pill.IsEffectivelyVisible);
        Assert.True(pill.Bounds.Height > 0);
        Assert.Equal(pageBefore, LayoutAudit.BoundsIn(harness.Window, page));
        Assert.Equal(contentBefore, LayoutAudit.BoundsIn(harness.Window, content));
        Assert.True(pageBefore.Contains(LayoutAudit.BoundsIn(harness.Window, pill)), "The pill floats over the page, not beside it.");

        // The room for the pill grows inside the page, under its pinned content; the shell around the page keeps its size.
        Assert.Equal(64, room.Bounds.Height);

        harness.Backend.Jobs.Complete(job.JobId);
        harness.Main.Toasts.DismissAll();
        harness.Settle();

        Assert.False(pill.IsEffectivelyVisible);
        Assert.Equal(pageBefore, LayoutAudit.BoundsIn(harness.Window, page));
        Assert.Equal(contentBefore, LayoutAudit.BoundsIn(harness.Window, content));
    }

    [AvaloniaFact]
    public async Task A_long_job_name_is_trimmed_so_the_pill_stays_short_of_the_actions_on_the_right()
    {
        using var harness = await ShellViewHarness.StartAsync(width: 640, height: 420);
        FakeAssert.Ok(await harness.Backend.Games.OptimizeStorageAsync(SampleScenario.SatisfactoryId));
        harness.Settle();
        var pill = harness.Find<Button>("Pill");
        var text = harness.Find<TextBlock>("PillText");

        harness.Shell.JobStatus.PillText = "Taking a snapshot of The Elder Scrolls V: Skyrim Special Edition, Anniversary Edition with every mod and every patch";
        harness.Settle();

        var shown = LayoutAudit.BoundsIn(harness.Window, pill);
        Assert.True(shown.Width <= 360, $"The pill is {shown.Width:0} px wide.");
        Assert.True(LayoutAudit.BoundsIn(harness.Window, text).Right <= shown.Right, "The text runs out of the pill.");
        Assert.Equal(TextTrimming.CharacterEllipsis, text.TextTrimming);
    }

    [AvaloniaFact]
    public async Task The_job_pill_is_gone_when_the_job_ends_and_a_toast_says_how_it_went()
    {
        using var harness = await ShellViewHarness.StartAsync();
        var job = FakeAssert.Ok(await harness.Backend.Profiles.CaptureAsync(SampleScenario.SkyrimId, SampleScenario.ModdedId, "A point"));

        harness.Backend.Jobs.Complete(job.JobId);
        harness.Settle();

        var texts = VisibleTexts(harness).ToList();
        Assert.DoesNotContain("Taking a snapshot of Skyrim Special Edition\u2026", texts);
        Assert.Contains("Took a snapshot of Skyrim Special Edition.", texts);
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
        using var harness = await ShellViewHarness.StartAsync(new FakeBackend(ScenarioBuilder.Sample().WithStartupStatus(status)));

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
        using var harness = await ShellViewHarness.StartAsync(new FakeBackend(ScenarioBuilder.Sample().WithStartupStatus(status)));

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
        using var harness = await ShellViewHarness.StartAsync(new FakeBackend(ScenarioBuilder.Sample().WithStartupStatus(status)));

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

        window.Show();

        Assert.Single(Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<MainView>());
        Assert.Equal(1040, window.Width);
    }

    private static Savepoint.App.Features.Common.Formatting.UiFormatter TextFor(ShellViewHarness harness) =>
        new(Savepoint.App.Localization.LocalizationCatalog.Current, harness.Backend.Clock, "windows", TimeZoneInfo.Utc);
}
