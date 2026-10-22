using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
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
        var scenario = (seeded ? RemovedSnapshotSamples.Scenario() : ScenarioBuilder.Mockup()).WithSettings(MockupScenario.Settings with { Theme = theme });
        var backend = new FakeBackend(scenario);
        var clock = backend.Clock;
        return ScreenHarness.StartAsync("history", context => [new HistoryScreenFactory(context, clock)], NavDestination.History, backend, theme, width, height, realDialogs);
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
        Assert.False(Named<ScrollViewer>(harness, "RemovedScroll").IsEffectivelyVisible);
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
        Assert.True(Named<ScrollViewer>(harness, "RemovedScroll").IsEffectivelyVisible);

        Screen(harness).IsRemovedOpen = false;
        harness.Settle();

        Assert.False(Named<ToggleButton>(harness, "RemovedToggle").IsChecked);
        Assert.False(Named<ScrollViewer>(harness, "RemovedScroll").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task An_open_section_says_when_the_snapshots_are_deleted_and_lists_each_row_with_a_button_named_after_it()
    {
        using var harness = await StartAsync();
        Open(harness);

        var texts = harness.VisibleTexts().ToList();

        Assert.Contains(texts, text => text is not null && text.Contains("deletes them for good", StringComparison.Ordinal));
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
    public async Task At_the_smallest_supported_windows_an_open_section_leaves_the_snapshots_room_and_stays_inside_the_window(double width, double height)
    {
        using var harness = await StartAsync(width: width, height: height);
        Open(harness);

        var section = Named<Border>(harness, "RemovedSection");
        var bottom = section.TranslatePoint(new Point(0, section.Bounds.Height), harness.View)!.Value.Y;

        Assert.True(bottom <= harness.View.Bounds.Height + 0.5, $"the section ends at {bottom}, below the window's {harness.View.Bounds.Height}");
        Assert.True(harness.Find<Panel>("PointsRegion").Bounds.Height >= 100, $"the snapshots keep only {harness.Find<Panel>("PointsRegion").Bounds.Height} px");
        Assert.True(Named<ScrollViewer>(harness, "RemovedScroll").Bounds.Height <= height * 0.5 + 1);
    }
}
