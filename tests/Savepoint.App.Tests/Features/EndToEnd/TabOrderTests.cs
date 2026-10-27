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

    private static string[] ProfileRows(string name, string play = "Play") => [$"Row: {name}", $"{play} {name}", $"More actions for {name}"];

    [AvaloniaFact]
    public async Task The_sidebar_layout_goes_rail_then_header_then_chips_then_the_profile_rows_then_the_footer()
    {
        using var app = await ComposedApp.StartWithViewAsync();

        Assert.Equal(
            [
                "Games", "Search games", "Skyrim Special Edition, 3 profiles", "Add games from your libraries", "Add a game",
                "Edit game", "New profile", "More actions for Skyrim Special Edition", "Sidebar",
                "Profiles", "History", "Activity", "Take a snapshot", "Search profiles",
                .. ProfileRows(_modded), .. ProfileRows(_vanilla, "Switch and play"), .. ProfileRows(_survival, "Switch and play"),
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
                "Games", "Library", "Search games",
                "Add games from your libraries", "Add a game", "Skyrim Special Edition, 3 profiles", "Add a game",
                "Edit game", "New profile", "More actions for Skyrim Special Edition", "Take a snapshot", "Search profiles",
                .. ProfileRows(_modded), .. ProfileRows(_vanilla, "Switch and play"), .. ProfileRows(_survival, "Switch and play"),
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
                "History", "Search games", "Skyrim Special Edition, 3 profiles", "Add games from your libraries", "Add a game",
                "What's a snapshot?", "Filter by profile: All profiles", "Cards", "Search snapshots", "All",
                "Go back and play: Cleared Blackreach", "Go back without playing: Cleared Blackreach",
                "Go back and play: 14 mod files updated", "Go back without playing: 14 mod files updated",
                "Go back and play: Fresh start with Nolvus", "Go back without playing: Fresh start with Nolvus",
                "Go back and play: Vanilla 1.6.1170", "Go back without playing: Vanilla 1.6.1170",
                "Go back and play: Deep cave run", "Go back without playing: Deep cave run",
                "Go back and play: First night", "Go back without playing: First night",
                "Go back and play: Survival test - 5-14-26", "Go back without playing: Survival test - 5-14-26",
            ],
            TabStops(app));
    }

    [AvaloniaFact]
    public async Task Activity_goes_rail_then_the_scope_then_the_jobs_then_cancel_copy_and_the_lines_then_the_job_pill()
    {
        var backend = FakeBackend.Sample();
        await ActivityScenario.AddJobsAsync(backend);
        using var app = await ComposedApp.StartWithViewAsync(backend);
        app.Shell.Navigate(NavDestination.Activity);
        app.Settle();

        var stops = TabStops(app);

        Assert.Equal(["Activity", "Search games", "Skyrim Special Edition, 3 profiles", "Add games from your libraries", "Add a game", "This game"], stops.Take(6));
        Assert.StartsWith("Taking a snapshot of Skyrim Special Edition. In progress.", stops[6], StringComparison.Ordinal);
        Assert.Equal(["Cancel Taking a snapshot of Skyrim Special Edition", "Copy details"], stops.Skip(7).Take(2));
        Assert.StartsWith("Taking snapshot", stops[9], StringComparison.Ordinal);
        Assert.Equal("Taking a snapshot of Skyrim Special Edition… Open Activity", stops[10]);
        Assert.Equal(11, stops.Count);
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
                "History", "Search games", "Skyrim Special Edition, 3 profiles", "Add games from your libraries", "Add a game",
                "What's a snapshot?", "Filter by profile: All profiles", "List", "Search snapshots", "All",
                .. app.History.Timeline.Where(entry => entry.CanGoBack).SelectMany(entry => new[] { entry.GoBackAutomationName, entry.SwitchAutomationName }),
                "Go back and play: Cleared Blackreach (saves)", "Go back without playing: Cleared Blackreach (saves)",
                "Go back and play: Fresh start with Nolvus (saves)", "Go back without playing: Fresh start with Nolvus (saves)",
                "Go back and play: Deep cave run (saves)", "Go back without playing: Deep cave run (saves)",
                "Go back and play: First night (saves)", "Go back without playing: First night (saves)",
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
                "Settings", "Search games", "Skyrim Special Edition, 3 profiles", "Add games from your libraries", "Add a game",
                "How SavePoint works", "Show the tour again",
                "Change", "Run now", "How much space do snapshots take?",
                "Dark", "100%", "Sidebar", "Blue", "Look up game artwork online",
                "Skip the check before playing when only your saves change", "Ask whether to keep the session",
                "Remove all data…", "Third-party notices",
            ],
            TabStops(app));
    }

    [AvaloniaFact]
    public async Task The_tour_card_has_focus_when_it_starts_and_Tab_goes_Skip_tour_then_Next_before_the_next_step()
    {
        using var app = await AppStates.OpenAsync("tour-step-1");

        var stops = TabStops(app);

        Assert.Equal(["Row: Tour, step 1 of 4: This is a profile", "Skip tour", "Next"], stops.Take(3));
        app.Shell.Tour.NextCommand.Execute(null);
        app.Settle();
        Assert.Equal(["Row: Tour, step 2 of 4: Play puts the right files in place", "Skip tour", "Back", "Next"], TabStops(app).Take(4));
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
        _ = app.Games.Profiles.Single(row => row.Id == SampleScenario.SurvivalId).PlayCommand.ExecuteAsync(null);
        app.Settle();

        Assert.Equal(
            ["Saves to load", "What's a save snapshot?", "What are new changes?", "Take a snapshot first", "Discard them", "Skip this check when only your saves change", "Cancel"],
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
        _ = app.Games.Profiles.Single(row => row.Id == SampleScenario.ModdedId).PlayCommand.ExecuteAsync(null);
        app.Backend.Launch.ReportRunning(SampleScenario.SkyrimId, SampleScenario.RunningSince);
        app.Backend.Launch.ReportExited(SampleScenario.SkyrimId, SampleScenario.PlayDuration, SampleScenario.NewSaveFiles);
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
