using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Savepoint.App.Composition;
using Savepoint.App.Features.History;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Theme;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.History;

// The "Recently removed" section as a person sees it: hidden when empty, closed until opened, reachable and readable without a mouse.
public class HistoryRecentlyRemovedViewTests
{
    private static Task<ScreenHarness> StartAsync(bool seeded = true, double width = 1040, double height = 660, ThemePreference theme = ThemePreference.Dark, bool realDialogs = false)
    {
        var scenario = (seeded ? RemovedSnapshotSamples.Scenario() : ScenarioBuilder.Sample()).WithSettings(SampleScenario.Settings with { Theme = theme });
        var backend = new FakeBackend(scenario);
        var clock = backend.Clock;
        return ScreenHarness.StartAsync("history", context => [new HistoryScreenFactory(FeatureComposition.HistoryDependenciesOf(context, clock))], NavDestination.History, backend, theme, width, height, realDialogs);
    }

    private static HistoryScreenViewModel Screen(ScreenHarness harness) => (HistoryScreenViewModel)harness.Shell.CurrentScreen!;

    // A control the view has to have; its absence is a failed assertion, not an exception from the lookup.
    private static T Named<T>(ScreenHarness harness, string name)
        where T : Control
    {
        var found = harness.All<T>().FirstOrDefault(control => control.Name == name);
        if (found is null)
        {
            Assert.Fail($"The History view has no {typeof(T).Name} named {name}.");
        }

        return found;
    }

    private static void Open(ScreenHarness harness)
    {
        Screen(harness).IsRemovedOpen = true;
        harness.Settle();
    }

    [AvaloniaFact]
    public async Task The_section_is_hidden_when_nothing_was_removed()
    {
        using var harness = await StartAsync(seeded: false);

        Assert.False(Named<Border>(harness, "RemovedSection").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task The_section_sits_under_the_snapshots_closed_with_its_heading_and_nothing_of_its_rows_showing()
    {
        using var harness = await StartAsync();

        var section = Named<Border>(harness, "RemovedSection");
        var points = harness.Find<Panel>("PointsRegion");

        Assert.True(section.IsEffectivelyVisible);
        Assert.Contains("Recently removed (2)", harness.VisibleTexts());
        Assert.False(Named<StackPanel>(harness, "RemovedContent").IsEffectivelyVisible);
        Assert.False(Named<ToggleButton>(harness, "RemovedToggle").IsChecked);
        Assert.DoesNotContain(harness.VisibleTexts(), text => text is not null && text.StartsWith("From \"", StringComparison.Ordinal));
        Assert.True(section.TranslatePoint(default, harness.View)!.Value.Y >= points.TranslatePoint(default, harness.View)!.Value.Y + points.Bounds.Height - 1, "the section sits below the snapshot list");
    }

    [AvaloniaFact]
    public async Task The_toggle_opens_the_section_and_its_state_follows_the_view_model()
    {
        using var harness = await StartAsync();

        Named<ToggleButton>(harness, "RemovedToggle").IsChecked = true;
        harness.Settle();

        Assert.True(Screen(harness).IsRemovedOpen);
        Assert.True(Named<StackPanel>(harness, "RemovedContent").IsEffectivelyVisible);

        Screen(harness).IsRemovedOpen = false;
        harness.Settle();

        Assert.False(Named<ToggleButton>(harness, "RemovedToggle").IsChecked);
        Assert.False(Named<StackPanel>(harness, "RemovedContent").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task An_open_section_says_when_the_snapshots_are_deleted_and_lists_each_row_with_a_button_named_after_it()
    {
        using var harness = await StartAsync();
        Open(harness);

        var texts = harness.VisibleTexts().ToList();

        Assert.Contains(texts, text => text is not null && text.Contains("deleted for good", StringComparison.Ordinal));
        Assert.Contains("Before the final boss", texts);
        Assert.Contains("Cleared Blackreach", texts);
        Assert.Contains("From \"Modded playthrough - 7-2-26\". Removed when you continued it from a session.", texts);
        Assert.Contains("Removed Jul 8, 10:00 AM", texts);
        var buttons = harness.All<Button>().Where(button => button.IsEffectivelyVisible && (button.Content as string) == "Bring back as a new profile").ToList();
        Assert.Equal(2, buttons.Count);
        Assert.Equal(
            ["Bring back as a new profile: Before the final boss", "Bring back as a new profile: Cleared Blackreach"],
            buttons.Select(button => AutomationProperties.GetName(button)));
    }

    [AvaloniaFact]
    public async Task The_list_and_its_rows_are_a_list_with_names_for_assistive_technology()
    {
        using var harness = await StartAsync();
        Open(harness);

        var list = Named<ItemsControl>(harness, "RemovedList");

        Assert.Equal(AutomationControlType.List, AutomationProperties.GetControlTypeOverride(list));
        Assert.Equal("Recently removed (2)", AutomationProperties.GetName(list));
        var rows = list.GetVisualDescendants().OfType<Border>().Where(border => AutomationProperties.GetControlTypeOverride(border) == AutomationControlType.ListItem).ToList();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(row))));
        Assert.Empty(AutomationAudit.Problems(harness.View));
    }

    [AvaloniaFact]
    public async Task The_section_works_from_the_keyboard_the_toggle_opens_with_space_and_each_button_is_a_tab_stop()
    {
        using var harness = await StartAsync();
        var toggle = Named<ToggleButton>(harness, "RemovedToggle");
        Assert.True(toggle.Focusable && toggle.IsTabStop);
        toggle.Focus(NavigationMethod.Tab);
        harness.Settle();

        harness.Window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
        harness.Window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
        harness.Settle();

        Assert.True(Screen(harness).IsRemovedOpen);
        var buttons = harness.All<Button>().Where(button => button.IsEffectivelyVisible && (button.Content as string) == "Bring back as a new profile").ToList();
        Assert.All(buttons, button => Assert.True(button.Focusable && button.IsTabStop && button.IsEnabled));
    }

    [AvaloniaFact]
    public async Task A_bring_back_button_opens_the_name_dialog_for_its_snapshot()
    {
        using var harness = await StartAsync();
        Open(harness);

        Assert.Single(harness.All<Button>(), button => AutomationProperties.GetName(button) == "Bring back as a new profile: Cleared Blackreach").Command!.Execute(null);
        harness.Settle();

        var dialog = Assert.IsType<BringBackDialogViewModel>(Assert.Single(harness.FakeDialogs.Shown));
        Assert.Equal("Cleared Blackreach", dialog.Name);
    }

    [AvaloniaTheory]
    [InlineData(960, 460)]
    [InlineData(910, 432)]
    public async Task At_the_smallest_supported_windows_an_open_section_leaves_the_snapshots_their_floor(double width, double height)
    {
        using var harness = await StartAsync(width: width, height: height);
        Open(harness);

        Assert.True(harness.Find<Panel>("PointsRegion").Bounds.Height >= _listFloor, $"the snapshots keep only {harness.Find<Panel>("PointsRegion").Bounds.Height} px");
    }

    [AvaloniaFact]
    public async Task An_open_explanation_takes_room_too_and_the_snapshots_still_keep_their_floor()
    {
        using var harness = await StartAsync(width: 1040, height: 560);
        Screen(harness).SnapshotHelp.IsOpen = true;
        Open(harness);

        Assert.True(harness.Find<Panel>("PointsRegion").Bounds.Height >= _listFloor, $"the snapshots keep only {harness.Find<Panel>("PointsRegion").Bounds.Height} px");
    }

    [AvaloniaFact]
    public async Task With_room_to_spare_the_snapshots_fill_the_screen_and_nothing_scrolls()
    {
        using var harness = await StartAsync(width: 1040, height: 900);
        Open(harness);

        var page = Named<ScrollViewer>(harness, "PageScroll");
        var points = harness.Find<Panel>("PointsRegion");
        var section = Named<Border>(harness, "RemovedSection");
        var bottom = section.TranslatePoint(new Point(0, section.Bounds.Height), harness.View)!.Value.Y;

        Assert.True(page.Extent.Height <= page.Viewport.Height + 1, $"the screen scrolls: {page.Extent.Height} px in {page.Viewport.Height}");
        Assert.True(points.Bounds.Height > _listFloor, $"the snapshots stay at the floor, {points.Bounds.Height} px");
        Assert.True(bottom <= harness.View.Bounds.Height + 0.5, $"the section ends at {bottom}, below the window's {harness.View.Bounds.Height}");
    }

    // Three rows of the snapshot list: its heading, then three saves of about 70 px each.
    private const double _listFloor = 220;

    [AvaloniaTheory]
    [InlineData(640, 420)]
    [InlineData(910, 432)]
    public async Task At_the_smallest_windows_with_the_explanation_and_the_section_both_open_the_list_keeps_three_rows_and_the_screen_scrolls_to_the_rest(double width, double height)
    {
        using var harness = await StartAsync(width: width, height: height);
        Screen(harness).SnapshotHelp.IsOpen = true;
        Open(harness);

        var points = harness.Find<Panel>("PointsRegion");
        Assert.True(points.Bounds.Height >= _listFloor, $"the snapshot list keeps only {points.Bounds.Height} px, under the {_listFloor} px floor");

        var page = Named<ScrollViewer>(harness, "PageScroll");
        Assert.True(page.Extent.Height > page.Viewport.Height + 1, $"the screen has {page.Extent.Height} px to show in {page.Viewport.Height}, so nothing scrolls");

        page.ScrollToEnd();
        harness.Settle();

        var window = new Rect(harness.View.Bounds.Size);
        foreach (var name in new[] { "Footer", "RemovedToggle", "RemovedList" })
        {
            var control = Named<Control>(harness, name);
            var bounds = new Rect(control.TranslatePoint(default, harness.View)!.Value, control.Bounds.Size);
            Assert.True(bounds.Bottom <= window.Bottom + 1 && bounds.Top >= -1, $"{name} at {bounds} is outside the {window.Size} window after scrolling to the end");
        }

        // The screen's own controls; the rail beside it is audited with the shell.
        Assert.Empty(LayoutAudit.Problems(harness.Window));
        Assert.Empty(AutomationAudit.Problems(page.FindAncestorOfType<HistoryScreenView>()!));
    }

    [AvaloniaFact]
    public async Task A_closed_explanation_takes_no_room_between_the_header_and_the_search_row_or_the_search_row_and_the_snapshots()
    {
        using var harness = await StartAsync(width: 910, height: 432);

        var header = Named<Border>(harness, "Header");
        var searchRow = Named<Border>(harness, "SearchRow");
        var headerBottom = header.TranslatePoint(new Point(0, header.Bounds.Height), harness.View)!.Value.Y;
        var searchTop = searchRow.TranslatePoint(default, harness.View)!.Value.Y;
        var searchBottom = searchRow.TranslatePoint(new Point(0, searchRow.Bounds.Height), harness.View)!.Value.Y;
        var pointsTop = harness.Find<Panel>("PointsRegion").TranslatePoint(default, harness.View)!.Value.Y;

        Assert.True(searchTop <= headerBottom + 0.5, $"the search row starts {searchTop - headerBottom} px under the header");
        Assert.True(pointsTop <= searchBottom + 0.5, $"the snapshots start {pointsTop - searchBottom} px under the search row");
    }
}
