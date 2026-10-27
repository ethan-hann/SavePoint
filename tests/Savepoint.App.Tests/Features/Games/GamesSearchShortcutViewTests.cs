using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Savepoint.App.Features.Common.Search;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.EndToEnd;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Games;

// Ctrl+Shift+F (Cmd+Shift+F on macOS) goes to the games search from anywhere; Ctrl+F still goes to the profile search on this screen.
public class GamesSearchShortcutViewTests
{
    private static TextBox FieldOf(ComposedApp app, string boxName) => app.Find<SearchBoxView>(boxName).GetVisualDescendants().OfType<TextBox>().Single();

    private static void PressGamesSearchKey(ComposedApp app) =>
        app.Press(Key.F, PhysicalKey.F, RawInputModifiers.Control | RawInputModifiers.Shift);

    [AvaloniaFact]
    public async Task Ctrl_Shift_F_on_the_Games_screen_moves_to_the_games_search_not_the_profile_search()
    {
        using var app = await ComposedApp.StartWithViewAsync();

        PressGamesSearchKey(app);

        Assert.Same(FieldOf(app, "RailSearch"), app.Focused);
    }

    [AvaloniaFact]
    public async Task Ctrl_F_still_moves_to_the_profile_search_after_the_games_search_had_focus()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        PressGamesSearchKey(app);

        app.Press(Key.F, PhysicalKey.F, RawInputModifiers.Control);

        Assert.Same(FieldOf(app, "ProfileSearchBox"), app.Focused);
    }

    [AvaloniaFact]
    public async Task Ctrl_Shift_F_moves_to_the_games_search_from_a_row_of_the_profile_table()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        var row = app.Find<ItemsControl>("Rows").GetVisualDescendants().OfType<Border>().First(border => border.Classes.Contains("table-row"));
        row.Focus();
        Assert.Same(row, app.Focused);

        PressGamesSearchKey(app);

        Assert.Same(FieldOf(app, "RailSearch"), app.Focused);
    }

    [AvaloniaFact]
    public async Task Ctrl_Shift_F_moves_to_the_games_search_from_the_profile_search()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        FieldOf(app, "ProfileSearchBox").Focus();

        PressGamesSearchKey(app);

        Assert.Same(FieldOf(app, "RailSearch"), app.Focused);
    }

    [AvaloniaFact]
    public async Task Ctrl_Shift_F_moves_to_the_shelf_search_in_the_library_layout()
    {
        using var app = await ComposedApp.StartWithViewAsync(layout: ShellLayout.Library);

        PressGamesSearchKey(app);

        Assert.Same(FieldOf(app, "ShelfSearch"), app.Focused);
    }

    [AvaloniaFact]
    public async Task Ctrl_Shift_F_moves_to_the_games_search_from_another_screen()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        app.Shell.Navigate(NavDestination.Settings);
        app.Settle();

        PressGamesSearchKey(app);

        Assert.Same(FieldOf(app, "RailSearch"), app.Focused);
    }

    [AvaloniaTheory]
    [InlineData(NavDestination.Settings)]
    [InlineData(NavDestination.History)]
    [InlineData(NavDestination.Activity)]
    public async Task Ctrl_Shift_F_in_the_library_layout_from_another_screen_brings_up_the_Games_screen_and_its_search(NavDestination from)
    {
        using var app = await ComposedApp.StartWithViewAsync(layout: ShellLayout.Library);
        app.Shell.Navigate(from);
        app.Settle();

        PressGamesSearchKey(app);

        Assert.Equal(NavDestination.Games, app.Shell.Destination);
        Assert.Same(FieldOf(app, "ShelfSearch"), app.Focused);
    }

    [AvaloniaFact]
    public async Task Ctrl_Shift_F_in_the_library_layout_does_nothing_while_a_dialog_is_open()
    {
        using var app = await ComposedApp.StartWithViewAsync(layout: ShellLayout.Library);
        app.Shell.Navigate(NavDestination.Settings);
        _ = app.Games.DeleteGameCommand.ExecuteAsync(null);
        app.Settle();

        PressGamesSearchKey(app);

        Assert.Equal(NavDestination.Settings, app.Shell.Destination);
    }

    [AvaloniaFact]
    public async Task Ctrl_Shift_F_leaves_the_icon_rail_alone_because_it_has_no_search_box()
    {
        using var app = await ComposedApp.StartWithViewAsync(width: 800);
        var before = app.Focused;

        PressGamesSearchKey(app);

        Assert.Same(before, app.Focused);
    }

    [AvaloniaFact]
    public async Task Ctrl_Shift_F_does_nothing_while_a_dialog_is_open()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        app.Shell.Navigate(NavDestination.Settings);
        _ = app.Games.DeleteGameCommand.ExecuteAsync(null);
        app.Settle();
        var before = app.Focused;

        PressGamesSearchKey(app);

        Assert.Same(before, app.Focused);
        Assert.NotSame(FieldOf(app, "RailSearch"), app.Focused);
    }

    [AvaloniaFact]
    public async Task Ctrl_Shift_F_does_nothing_while_the_tour_runs_and_works_again_once_it_ends()
    {
        var backend = new FakeBackend(ScenarioBuilder.Sample().WithSettings(SampleScenario.Settings with { TourSeen = false }));
        using var app = await ComposedApp.StartWithViewAsync(backend);
        Assert.True(app.Shell.ShowTour);
        var before = app.Focused;

        PressGamesSearchKey(app);

        Assert.Same(before, app.Focused);
        Assert.NotSame(FieldOf(app, "RailSearch"), app.Focused);

        app.Shell.Tour.SkipCommand.Execute(null);
        app.Settle();
        PressGamesSearchKey(app);

        Assert.Same(FieldOf(app, "RailSearch"), app.Focused);
    }

    [AvaloniaTheory]
    [InlineData("RailSearch", "Search games (Ctrl+Shift+F)", "Ctrl+Shift+F")]
    [InlineData("ProfileSearchBox", "Search profiles (Ctrl+F)", "Ctrl+F")]
    public async Task A_search_box_shows_its_key_in_its_tooltip_and_tells_assistive_technology(string boxName, string tip, string key)
    {
        using var app = await ComposedApp.StartWithViewAsync();
        var field = FieldOf(app, boxName);

        Assert.Equal(tip, ToolTip.GetTip(field));
        Assert.Equal(key, AutomationProperties.GetAcceleratorKey(field));
    }

    [AvaloniaFact]
    public async Task The_shelf_search_shows_its_key_in_its_tooltip()
    {
        using var app = await ComposedApp.StartWithViewAsync(layout: ShellLayout.Library);

        Assert.Equal("Search games (Ctrl+Shift+F)", ToolTip.GetTip(FieldOf(app, "ShelfSearch")));
    }

    [AvaloniaFact]
    public async Task Typing_after_the_shortcut_goes_into_the_games_search()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        PressGamesSearchKey(app);

        app.Window.KeyTextInput("satis");
        app.Settle();

        Assert.Equal("satis", app.Shell.GameSearch.Text);
        Assert.Equal(string.Empty, app.Games.ProfileSearch.Text);
    }
}
