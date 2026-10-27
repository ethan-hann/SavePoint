using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Savepoint.App.Features.Common.Accessibility;
using Savepoint.App.Features.Common.Search;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Features.EndToEnd;
using Savepoint.App.Tests.Theme;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Shell;

public class ShellGameSearchViewTests
{
    private static TextBox FieldOf(ComposedApp app, string boxName) => app.Find<SearchBoxView>(boxName).GetVisualDescendants().OfType<TextBox>().Single();

    private static List<string?> Listed(ComposedApp app, string listName) =>
        [.. app.Find<ListBox>(listName).GetVisualDescendants().OfType<ListBoxItem>().Select(item => AutomationAudit.NameOf(item))];

    private static void Type(ComposedApp app, string text)
    {
        app.Window.KeyTextInput(text);
        app.Settle();
    }

    [AvaloniaFact]
    public async Task Ctrl_F_on_a_screen_with_no_search_of_its_own_moves_to_the_games_search()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        app.Shell.Navigate(NavDestination.Settings);
        app.Settle();

        app.Press(Key.F, PhysicalKey.F, RawInputModifiers.Control);

        Assert.Same(FieldOf(app, "RailSearch"), app.Focused);
    }

    [AvaloniaFact]
    public async Task Ctrl_F_does_nothing_while_a_dialog_is_open()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        app.Shell.Navigate(NavDestination.Settings);
        _ = app.Games.DeleteGameCommand.ExecuteAsync(null);
        app.Settle();
        var before = app.Focused;

        app.Press(Key.F, PhysicalKey.F, RawInputModifiers.Control);

        Assert.Same(before, app.Focused);
        Assert.NotSame(FieldOf(app, "RailSearch"), app.Focused);
    }

    [AvaloniaFact]
    public async Task Typing_in_the_rail_search_lists_only_the_games_that_match()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        FieldOf(app, "RailSearch").Focus();

        Type(app, "satis");

        Assert.Equal(["Satisfactory, 2 profiles"], Listed(app, "RailGames"));
    }

    [AvaloniaFact]
    public async Task Escape_clears_the_search_and_leaves_the_cursor_in_the_box()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        var field = FieldOf(app, "RailSearch");
        field.Focus();
        Type(app, "satis");

        app.Press(Key.Escape, PhysicalKey.Escape);

        Assert.Equal(string.Empty, app.Shell.GameSearch.Text);
        Assert.Equal(3, Listed(app, "RailGames").Count);
        Assert.Same(field, app.Focused);
    }

    [AvaloniaFact]
    public async Task The_clear_button_shows_only_with_text_and_clearing_returns_the_cursor_to_the_box()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        var field = FieldOf(app, "RailSearch");
        var clear = app.Find<SearchBoxView>("RailSearch").GetVisualDescendants().OfType<Button>().Single();
        Assert.False(clear.IsEffectivelyVisible);
        field.Focus();
        Type(app, "satis");
        Assert.True(clear.IsEffectivelyVisible);
        clear.Focus();

        new ButtonAutomationPeer(clear).Invoke();
        app.Settle();

        Assert.Equal(string.Empty, field.Text);
        Assert.False(clear.IsEffectivelyVisible);
        Assert.Same(field, app.Focused);
    }

    [AvaloniaFact]
    public async Task The_box_and_its_clear_button_have_names_a_screen_reader_can_say()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        var box = app.Find<SearchBoxView>("RailSearch");
        FieldOf(app, "RailSearch").Focus();
        Type(app, "sat");

        Assert.Equal("Search games", AutomationAudit.NameOf(FieldOf(app, "RailSearch")));
        Assert.Equal("Clear search", AutomationAudit.NameOf(box.GetVisualDescendants().OfType<Button>().Single()));
        Assert.Empty(AutomationAudit.Problems(app.View!));
    }

    [AvaloniaFact]
    public async Task A_search_nothing_matches_says_so_with_a_way_to_clear_it()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        FieldOf(app, "RailSearch").Focus();
        Type(app, "zzz");

        var line = app.Find<StackPanel>("RailNoMatches");
        Assert.True(line.IsEffectivelyVisible);
        Assert.Equal("No games match.", line.GetVisualDescendants().OfType<TextBlock>().First().Text);

        new ButtonAutomationPeer(line.GetVisualDescendants().OfType<Button>().Single()).Invoke();
        app.Settle();

        Assert.False(line.IsEffectivelyVisible);
        Assert.Equal(3, Listed(app, "RailGames").Count);
    }

    [AvaloniaFact]
    public async Task The_count_left_is_said_through_a_live_region_once_typing_pauses()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        var region = app.Find<SearchBoxView>("RailSearch").GetVisualDescendants().OfType<AnnouncementRegion>().Single();
        var spoken = new List<string?>();
        ControlAutomationPeer.CreatePeerForElement(region).PropertyChanged += (_, e) =>
        {
            if (e.Property == AutomationElementIdentifiers.NameProperty && e.NewValue is string { Length: > 0 } name)
            {
                spoken.Add(name);
            }
        };
        FieldOf(app, "RailSearch").Focus();

        Type(app, "sa");
        Type(app, "t");
        Assert.Empty(spoken);
        app.Clock.Advance(SearchBoxViewModel.AnnouncePause);
        app.Settle();

        Assert.Equal(AutomationLiveSetting.Polite, ControlAutomationPeer.CreatePeerForElement(region).GetLiveSetting());
        Assert.Equal(["1 of 3 games"], spoken);
    }

    [AvaloniaFact]
    public async Task The_icon_rail_has_no_search_box()
    {
        using var app = await ComposedApp.StartWithViewAsync(width: 800);

        Assert.False(app.Find<SearchBoxView>("RailSearch").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task The_library_shelf_has_its_own_search_that_narrows_the_shelf()
    {
        using var app = await ComposedApp.StartWithViewAsync(layout: ShellLayout.Library);
        Assert.Equal(["ShelfSearch", "ProfileSearchBox"], app.All<SearchBoxView>().Where(box => box.IsEffectivelyVisible).Select(box => box.Name));
        app.Shell.Navigate(NavDestination.Settings);
        app.Shell.Navigate(NavDestination.Games);
        app.Settle();

        app.Press(Key.F, PhysicalKey.F, RawInputModifiers.Control);
        Assert.Same(app.Find<SearchBoxView>("ProfileSearchBox").GetVisualDescendants().OfType<TextBox>().Single(), app.Focused);

        FieldOf(app, "ShelfSearch").Focus();
        Type(app, "last");
        Assert.Equal(["The Last Caretaker, 1 profile"], Listed(app, "ShelfGames"));
    }

    [AvaloniaFact]
    public async Task The_shelf_search_does_not_stretch_across_a_wide_window()
    {
        using var app = await ComposedApp.StartWithViewAsync(layout: ShellLayout.Library, width: 1600);

        var box = app.Find<SearchBoxView>("ShelfSearch");

        Assert.True(box.IsEffectivelyVisible);
        Assert.InRange(box.Bounds.Width, 140, 420);
    }

    [AvaloniaFact]
    public async Task Renders_the_rail_and_the_shelf_with_a_search_and_with_no_matches()
    {
        using var rail = await ComposedApp.StartWithViewAsync();
        rail.Shell.GameSearch.Text = "sa";
        Assert.True(File.Exists(rail.Render("shell", "rail-search")));

        using var empty = await ComposedApp.StartWithViewAsync();
        empty.Shell.GameSearch.Text = "zzz";
        Assert.True(File.Exists(empty.Render("shell", "rail-search-empty")));

        using var shelf = await ComposedApp.StartWithViewAsync(layout: ShellLayout.Library);
        shelf.Shell.GameSearch.Text = "sa";
        Assert.True(File.Exists(shelf.Render("shell", "shelf-search")));

        using var shelfEmpty = await ComposedApp.StartWithViewAsync(layout: ShellLayout.Library);
        shelfEmpty.Shell.GameSearch.Text = "zzz";
        Assert.True(File.Exists(shelfEmpty.Render("shell", "shelf-search-empty")));
    }

    [AvaloniaFact]
    public async Task The_search_survives_a_trip_to_another_screen()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        app.Shell.GameSearch.Text = "satis";

        app.Shell.Navigate(NavDestination.Settings);
        app.Shell.Navigate(NavDestination.Games);
        app.Settle();

        Assert.Equal("satis", FieldOf(app, "RailSearch").Text);
        Assert.Equal(["Satisfactory, 2 profiles"], Listed(app, "RailGames"));
    }
}
