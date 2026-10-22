using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.Activity;
using Savepoint.App.Tests.Theme;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.EndToEnd;

// Tab moves through a screen the way it reads: left to right, top to bottom, the rail before the page and a dialog's footer last.
public class TabOrderTests
{
    private const string _modded = "Modded playthrough - 7-2-26";
    private const string _vanilla = "Vanilla - latest update";
    private const string _survival = "Survival test - 5-14-26";

    // What a screen reader would say for each stop; a profile row is named by its profile.
    private static string StopName(InputElement element)
    {
        var name = element is Control control ? AutomationAudit.NameOf(control) ?? "(no name)" : "(not a control)";
        return element is Border ? $"Row: {name.Split(". ")[0]}" : name;
    }

    // The stops in the order Tab visits them, from where focus is now until it comes back round.
    private static List<string> TabStops(ComposedApp app, int limit = 60)
    {
        var stops = new List<string>();
        var first = app.Focused;
        if (first is not null)
        {
            stops.Add(StopName(first));
        }

        for (var i = 0; i < limit; i++)
        {
            app.Press(Key.Tab, PhysicalKey.Tab);
            var now = app.Focused ?? throw new InvalidOperationException("Focus was lost.");
            if (first is null)
            {
                first = now;
            }
            else if (ReferenceEquals(now, first))
            {
                return stops;
            }

            stops.Add(StopName(now));
        }

        throw new InvalidOperationException($"Tab never came back round: {string.Join(" > ", stops)}");
    }

    private static string[] ProfileRows(string name) => [$"Row: {name}", $"Play {name}", $"More actions for {name}"];

    [AvaloniaFact]
    public async Task The_sidebar_layout_goes_rail_then_header_then_chips_then_the_profile_rows_then_the_footer()
    {
        using var app = await ComposedApp.StartWithViewAsync();

        Assert.Equal(
            [
                "Games", "Skyrim Special Edition, 3 profiles", "Add a game",
                "Edit game", "New profile", "More actions for Skyrim Special Edition", "Sidebar",
                "Profiles", "History", "Activity",
                .. ProfileRows(_modded), .. ProfileRows(_vanilla), .. ProfileRows(_survival),
                "Free up space for Skyrim Special Edition",
            ],
            TabStops(app));
    }

    [AvaloniaFact]
    public async Task The_library_layout_goes_top_bar_then_shelf_then_header_then_the_profile_rows()
    {
        using var app = await ComposedApp.StartWithViewAsync(layout: ShellLayout.Library);

        Assert.Equal(
            [
                "Games", "Library",
                "Add a game", "Skyrim Special Edition, 3 profiles", "Add a game",
                "Edit game", "New profile", "More actions for Skyrim Special Edition",
                .. ProfileRows(_modded), .. ProfileRows(_vanilla), .. ProfileRows(_survival),
            ],
            TabStops(app));
    }

    [AvaloniaFact]
    public async Task History_cards_go_row_by_row_after_the_filter_and_the_view_toggle()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        app.Shell.Navigate(NavDestination.History);
        app.Settle();

        Assert.Equal(
            [
                "History", "Skyrim Special Edition, 3 profiles", "Add a game",
                "Filter by profile: All profiles", "Cards",
                "Go back to this: Cleared Blackreach", "Go back to this: 14 mod files updated",
                "Go back to this: Fresh start with Nolvus", "Go back to this: Vanilla 1.6.1170", "Go back to this: Deep cave run",
                "Go back to this: First night", "Go back to this: Survival test - 5-14-26",
            ],
            TabStops(app));
    }

    [AvaloniaFact]
    public async Task Activity_goes_rail_then_the_scope_then_the_jobs_then_cancel_copy_and_the_lines_then_the_job_pill()
    {
        var backend = FakeBackend.Mockup();
        await ActivityScenario.AddJobsAsync(backend);
        using var app = await ComposedApp.StartWithViewAsync(backend);
        app.Shell.Navigate(NavDestination.Activity);
        app.Settle();

        var stops = TabStops(app);

        Assert.Equal(["Activity", "Skyrim Special Edition, 3 profiles", "Add a game", "This game"], stops.Take(4));
        Assert.StartsWith("Taking a snapshot of Skyrim Special Edition. In progress.", stops[4], StringComparison.Ordinal);
        Assert.Equal(["Cancel Taking a snapshot of Skyrim Special Edition", "Copy details"], stops.Skip(5).Take(2));
        Assert.StartsWith("Taking the snapshot", stops[7], StringComparison.Ordinal);
        Assert.Equal("Taking a snapshot of Skyrim Special Edition… Open Activity", stops[8]);
        Assert.Equal(9, stops.Count);
    }

    [AvaloniaFact]
    public async Task The_history_list_goes_through_the_versions_then_the_saves_after_the_filter_and_the_view_toggle()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        app.Shell.Navigate(NavDestination.History);
        app.History.SelectedViewOption = app.History.ViewOptions[1];
        app.Settle();

        Assert.Equal(
            [
                "History", "Skyrim Special Edition, 3 profiles", "Add a game",
                "Filter by profile: All profiles", "List",
                .. app.History.Timeline.Where(entry => entry.CanGoBack).Select(entry => entry.GoBackAutomationName),
                "Go back to Cleared Blackreach", "Go back to Fresh start with Nolvus", "Go back to Deep cave run", "Go back to First night",
            ],
            TabStops(app));
    }

    [AvaloniaFact]
    public async Task Settings_goes_down_the_sections_in_the_order_they_are_drawn()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        app.Shell.Navigate(NavDestination.Settings);
        app.Settle();

        Assert.Equal(
            [
                "Settings", "Skyrim Special Edition, 3 profiles", "Add a game",
                "How SavePoint works",
                "Change", "Run now",
                "Dark", "Sidebar", "Blue", "Look up game artwork online",
                "Skip the check before playing when only your saves change", "Ask whether to keep the session",
                "Remove all data…", "Third-party notices",
            ],
            TabStops(app));
    }

    [AvaloniaFact]
    public async Task Settings_has_no_notices_stop_when_the_notices_file_was_not_shipped()
    {
        using var app = await ComposedApp.StartWithViewAsync(noticesShipped: false);
        app.Shell.Navigate(NavDestination.Settings);
        app.Settle();

        Assert.DoesNotContain("Third-party notices", TabStops(app));
    }

    [AvaloniaFact]
    public async Task The_launch_dialog_goes_from_the_save_to_load_through_the_unsaved_changes_to_the_footer()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        _ = app.Games.Profiles.Single(row => row.Id == MockupScenario.SurvivalId).PlayCommand.ExecuteAsync(null);
        app.Settle();

        Assert.Equal(
            ["Saves to load", "Take a snapshot first", "Discard them", "Skip this check when only your saves change", "Cancel"],
            TabStops(app));
    }

    [AvaloniaFact]
    public async Task The_wizard_goes_name_notes_then_its_buttons()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        _ = app.Games.NewProfileCommand.ExecuteAsync(null);
        app.Settle();

        Assert.Equal(["Profile name", "Notes (optional)", "Cancel", "Continue"], TabStops(app));
    }

    [AvaloniaFact]
    public async Task A_typed_confirmation_goes_from_the_field_to_cancel_and_stops_at_the_locked_confirm_button()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        _ = app.Games.DeleteGameCommand.ExecuteAsync(null);
        app.Settle();

        Assert.Equal(["Type Skyrim Special Edition to confirm.", "Cancel"], TabStops(app));
    }

    [AvaloniaFact]
    public async Task The_post_play_dialog_goes_through_the_choices_and_ends_on_its_buttons()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        _ = app.Games.Profiles.Single(row => row.Id == MockupScenario.ModdedId).PlayCommand.ExecuteAsync(null);
        app.Backend.Launch.ReportRunning(MockupScenario.SkyrimId, MockupScenario.RunningSince);
        app.Backend.Launch.ReportExited(MockupScenario.SkyrimId, MockupScenario.PlayDuration, MockupScenario.NewSaveFiles);
        app.Settle();

        var stops = TabStops(app);

        Assert.Equal(["Not now", "Keep it"], stops[^2..]);
        Assert.True(stops.Count > 2, "The choices come before the buttons.");
    }

    [AvaloniaFact]
    public async Task The_game_editor_goes_from_the_back_arrow_to_the_form_in_the_order_it_is_drawn()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        app.Games.EditGameCommand.Execute(null);
        app.Settle();

        var stops = TabStops(app);

        var back = stops.IndexOf("Back");
        Assert.True(back >= 0, "The back arrow is a tab stop.");
        Assert.Equal(
            ["Back", "Export", "Save changes", "Name", "Where the game is installed"],
            Enumerable.Range(back, 5).Select(i => stops[i % stops.Count]));
    }
}
