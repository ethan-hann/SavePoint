using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Savepoint.App.Features.History;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.EndToEnd;
using Savepoint.Core.Api;

namespace Savepoint.App.Tests.Features.History;

// A game with thousands of snapshots must draw in the time a handful of them would: the lists build only the cards on screen.
public class HistoryVirtualizationTests
{
    private const int _snapshots = 3000;
    private const int _removed = 400;

    // With only the visible cards built, the slowest step here takes about 0.2 s on a developer machine; building every card took
    // 4 to 10 s. The budget leaves a slower runner room and still fails by a wide margin when every card is built.
    private static readonly TimeSpan _budget = TimeSpan.FromSeconds(1.5);

    private static FakeBackend ManySnapshots()
    {
        var scenario = ScenarioBuilder.Sample();
        for (var i = 0; i < _snapshots; i++)
        {
            var kind = (i % 3) switch
            {
                0 => HistoryPointKind.VersionAndMods,
                1 => HistoryPointKind.SaveOnly,
                _ => HistoryPointKind.VersionModsAndSave,
            };
            scenario.WithHistoryPoint(
                SampleScenario.SkyrimId,
                new HistoryPoint(new HistoryPointId($"bulk-{i}"), kind, $"Session {i:0000}", ScenarioBuilder.DefaultNow.AddHours(-i - 1), SampleScenario.ModdedId, "Modded playthrough - 7-2-26", new InstallState("1.6.640", 212), null, null, false, false));
        }

        for (var i = 0; i < _removed; i++)
        {
            scenario.WithRemovedSnapshot(
                SampleScenario.SkyrimId,
                new RemovedSnapshot(new HistoryPointId($"gone-{i}"), HistoryPointKind.SaveOnly, $"Old session {i:0000}", ScenarioBuilder.DefaultNow.AddDays(-30).AddHours(-i), new InstallState("1.6.640", 212), SampleScenario.ModdedId, "Modded playthrough - 7-2-26", ScenarioBuilder.DefaultNow.AddHours(-i - 1), RemovedSnapshotReason.ContinuedFromSession));
        }

        return new FakeBackend(scenario);
    }

    private static async Task<(ComposedApp App, HistoryScreenViewModel Screen)> OpenAsync(bool list = false)
    {
        var app = await ComposedApp.StartWithViewAsync(ManySnapshots(), visitScreens: false);
        var screen = app.Show<HistoryScreenViewModel>(NavDestination.History);
        if (list)
        {
            screen.SelectedViewOption = screen.ViewOptions[1];
        }

        Layout(app);
        return (app, screen);
    }

    private static void Layout(ComposedApp app)
    {
        app.Settle();
        app.Window.UpdateLayout();
    }

    private static TimeSpan Time(Action action)
    {
        var watch = Stopwatch.StartNew();
        action();
        return watch.Elapsed;
    }

    private static void AssertWithinBudget(string what, TimeSpan took) =>
        Assert.True(took < _budget, $"{what} took {took.TotalMilliseconds:0} ms; the budget is {_budget.TotalMilliseconds:0} ms.");

    [AvaloniaFact]
    public async Task Thousands_of_snapshots_open_in_the_cards_view_within_the_budget()
    {
        using var app = await ComposedApp.StartWithViewAsync(ManySnapshots(), visitScreens: false);

        var took = Time(() =>
        {
            app.Show<HistoryScreenViewModel>(NavDestination.History);
            Layout(app);
        });

        AssertWithinBudget("Opening History with the cards", took);
    }

    [AvaloniaFact]
    public async Task Thousands_of_snapshots_open_in_the_list_view_within_the_budget()
    {
        using var app = await ComposedApp.StartWithViewAsync(ManySnapshots(), visitScreens: false);
        var screen = app.Show<HistoryScreenViewModel>(NavDestination.History);

        var took = Time(() =>
        {
            screen.SelectedViewOption = screen.ViewOptions[1];
            Layout(app);
        });

        AssertWithinBudget("Switching to the list", took);
    }

    [AvaloniaFact]
    public async Task Narrowing_a_search_to_one_card_and_clearing_it_stay_within_the_budget()
    {
        var (app, screen) = await OpenAsync();
        using var _ = app;

        var narrowed = Time(() =>
        {
            screen.Search.Text = "session 0299";
            app.Clock.Advance(HistoryScreenViewModel.TypingPause);
            Layout(app);
        });
        var cleared = Time(() =>
        {
            screen.Search.Text = string.Empty;
            app.Clock.Advance(HistoryScreenViewModel.TypingPause);
            Layout(app);
        });

        Assert.True(
            narrowed < _budget && cleared < _budget,
            $"Narrowing took {narrowed.TotalMilliseconds:0} ms and clearing took {cleared.TotalMilliseconds:0} ms; the budget is {_budget.TotalMilliseconds:0} ms.");
    }

    [AvaloniaFact]
    public async Task Opening_a_long_Recently_removed_section_stays_within_the_budget()
    {
        var (app, screen) = await OpenAsync();
        using var _ = app;

        var took = Time(() =>
        {
            screen.IsRemovedOpen = true;
            Layout(app);
        });

        AssertWithinBudget("Opening Recently removed", took);
    }

    [AvaloniaFact]
    public async Task Only_the_cards_near_the_screen_are_built()
    {
        var (app, _) = await OpenAsync();
        using var _ = app;

        var cards = app.Find<ItemsControl>("GroupList").GetVisualDescendants().OfType<Border>().Count(border => border.Classes.Contains("card"));

        Assert.InRange(cards, 1, 100);
    }

    [AvaloniaFact]
    public async Task Only_the_rows_near_the_screen_are_built_in_both_columns()
    {
        var (app, _) = await OpenAsync(list: true);
        using var _ = app;

        var entries = app.Find<ItemsControl>("TimelineList").GetVisualDescendants().OfType<Button>().Count();
        var saves = app.Find<ItemsControl>("SavesList").GetVisualDescendants().OfType<Border>().Count(border => border.Classes.Contains("save-row"));

        Assert.InRange(entries, 1, 100);
        Assert.InRange(saves, 1, 100);
    }

    [AvaloniaFact]
    public async Task Only_the_removed_rows_near_the_screen_are_built()
    {
        var (app, screen) = await OpenAsync();
        using var _ = app;
        screen.IsRemovedOpen = true;
        Layout(app);

        var rows = app.Find<ItemsControl>("RemovedList").GetVisualDescendants().OfType<Border>().Count(border => border.Classes.Contains("save-row"));

        Assert.InRange(rows, 1, 100);
    }

    // The buttons of the cards in the order Tab visits them; the loaded card has none.
    private static List<System.Windows.Input.ICommand> TabOrder(HistoryScreenViewModel screen) =>
        [.. screen.Groups.SelectMany(group => group.Cards).Where(card => card.CanGoBack).SelectMany(card => new[] { card.GoBackCommand, card.SwitchCommand })];

    private static void Tab(ComposedApp app, int times, bool back = false)
    {
        for (var press = 0; press < times; press++)
        {
            app.Press(Key.Tab, PhysicalKey.Tab, back ? RawInputModifiers.Shift : RawInputModifiers.None);
        }
    }

    [AvaloniaFact]
    public async Task Tab_walks_on_past_the_cards_that_are_built_so_a_card_far_down_can_be_reached()
    {
        var (app, screen) = await OpenAsync();
        using var _ = app;
        var order = TabOrder(screen);
        app.All<Button>().First(button => ReferenceEquals(button.Command, order[0])).Focus(NavigationMethod.Tab);

        // Sixty presses is well past the two or three rows of cards that fit on screen.
        Tab(app, 60);

        var focused = Assert.IsType<Button>(app.Focused);
        Assert.Same(order[60], focused.Command);
        Assert.True(focused.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task Shift_Tab_walks_back_to_the_cards_before_the_built_ones()
    {
        var (app, screen) = await OpenAsync();
        using var _ = app;
        var order = TabOrder(screen);
        app.All<Button>().First(button => ReferenceEquals(button.Command, order[0])).Focus(NavigationMethod.Tab);
        Tab(app, 60);

        Tab(app, 40, back: true);

        Assert.Same(order[20], Assert.IsType<Button>(app.Focused).Command);
    }

    private static void AssertTabReaches(ComposedApp app, IReadOnlyList<System.Windows.Input.ICommand> order, int presses)
    {
        ButtonOf(app, order[0]).Focus(NavigationMethod.Tab);

        Tab(app, presses);

        Assert.Same(order[presses], Assert.IsType<Button>(app.Focused).Command);
    }

    [AvaloniaFact]
    public async Task Tab_from_the_last_card_leaves_the_cards_instead_of_circling_back_into_them()
    {
        var (app, screen) = await OpenAsync();
        using var _ = app;
        var order = TabOrder(screen);
        var lastCard = screen.Groups.SelectMany(group => group.Cards).Last(card => card.CanGoBack);
        ScrollToCard(app, screen, lastCard.Name);
        ButtonOf(app, order[^1]).Focus(NavigationMethod.Tab);

        Tab(app, 1);

        var list = app.Find<ItemsControl>("GroupList");
        Assert.False(list.IsVisualAncestorOf(app.Focused), "Tab stayed inside the cards.");
    }

    [AvaloniaFact]
    public async Task The_cards_that_are_built_stay_in_the_order_of_the_list_after_jumping_to_the_end()
    {
        var (app, screen) = await OpenAsync();
        using var _ = app;
        var list = app.Find<ItemsControl>("GroupList");

        list.ScrollIntoView(list.ItemCount - 1);
        Layout(app);

        var indexes = list.ItemsPanelRoot!.Children.Select(child => list.IndexFromContainer((Control)child)).ToList();
        Assert.True(indexes.Count > 1);
        Assert.Equal(indexes.Order(), indexes);
    }

    [AvaloniaFact]
    public async Task Tab_walks_down_the_timeline_past_the_entries_that_are_built()
    {
        var (app, screen) = await OpenAsync(list: true);
        using var _ = app;

        AssertTabReaches(app, [.. screen.Timeline.Where(entry => entry.CanGoBack).SelectMany(entry => new[] { entry.GoBackCommand, entry.SwitchCommand })], 40);
    }

    [AvaloniaFact]
    public async Task Tab_walks_down_the_saves_past_the_rows_that_are_built()
    {
        var (app, screen) = await OpenAsync(list: true);
        using var _ = app;

        AssertTabReaches(app, [.. screen.Saves.Where(save => save.CanLoad).SelectMany(save => new[] { save.LoadCommand, save.SwitchCommand })], 40);
    }

    [AvaloniaFact]
    public async Task Tab_walks_down_a_long_Recently_removed_list_past_the_rows_that_are_built()
    {
        var (app, screen) = await OpenAsync();
        using var _ = app;
        screen.IsRemovedOpen = true;
        Layout(app);

        AssertTabReaches(app, [.. screen.Removed.Select(row => row.BringBackCommand)], 30);
    }

    private static Button ButtonOf(ComposedApp app, System.Windows.Input.ICommand command) =>
        app.All<Button>().Single(candidate => ReferenceEquals(candidate.Command, command));

    // The scroll area the cards sit in, and whether a control is inside what it shows.
    private static bool InView(ComposedApp app, Control control)
    {
        var area = app.Find<ScrollViewer>("CardsRegion");
        var top = control.TranslatePoint(default, area)!.Value.Y;
        return top >= -1 && top + control.Bounds.Height <= area.Bounds.Height + 1;
    }

    private static void ScrollToCard(ComposedApp app, HistoryScreenViewModel screen, string name)
    {
        var index = screen.CardEntries.Select((entry, at) => (entry, at)).First(pair => pair.entry is CardRowViewModel row && row.Cards.Any(card => card.Name == name)).at;
        app.Find<ItemsControl>("GroupList").ScrollIntoView(index);
        Layout(app);
    }

    [AvaloniaFact]
    public async Task A_focused_card_keeps_keyboard_focus_while_a_search_narrows_the_cards_around_it()
    {
        var (app, screen) = await OpenAsync();
        using var _ = app;
        var target = screen.Groups.SelectMany(group => group.Cards).Single(card => card.Name == "Session 0002");
        ButtonOf(app, target.GoBackCommand).Focus(NavigationMethod.Tab);

        screen.Search.Text = "session 000";
        app.Clock.Advance(HistoryScreenViewModel.TypingPause);
        Layout(app);

        Assert.Same(target.GoBackCommand, Assert.IsType<Button>(app.Focused).Command);
        Assert.True(InView(app, (Button)app.Focused!));
    }

    // A drawn control can be reused for another snapshot while it holds focus; the person is then on a card they never chose.
    [AvaloniaFact]
    public async Task Focus_that_ends_up_on_another_snapshots_control_goes_back_to_the_snapshot_it_was_on()
    {
        var (app, screen) = await OpenAsync();
        using var _ = app;
        var cards = screen.Groups.SelectMany(group => group.Cards).ToList();
        var target = cards.Single(card => card.Name == "Session 0002");
        var other = cards.Single(card => card.Name == "Session 0001");
        ButtonOf(app, target.GoBackCommand).Focus(NavigationMethod.Tab);

        // Runs after the view has noted where focus was, and before the cards change.
        screen.EntriesChanging += (_, _) => ButtonOf(app, other.GoBackCommand).Focus(NavigationMethod.Tab);
        screen.Search.Text = "session 000";
        app.Clock.Advance(HistoryScreenViewModel.TypingPause);
        Layout(app);

        Assert.Same(target.GoBackCommand, Assert.IsType<Button>(app.Focused).Command);
    }

    [AvaloniaFact]
    public async Task A_card_far_down_keeps_keyboard_focus_through_a_refresh_and_stays_in_view()
    {
        var (app, screen) = await OpenAsync();
        using var _ = app;
        ScrollToCard(app, screen, "Session 2500");
        var far = screen.Groups.SelectMany(group => group.Cards).Single(card => card.Name == "Session 2500");
        ButtonOf(app, far.SwitchCommand).Focus(NavigationMethod.Tab);

        await screen.ReloadAsync();
        Layout(app);

        var focused = Assert.IsType<Button>(app.Focused);
        var now = screen.Groups.SelectMany(group => group.Cards).Single(card => card.Name == "Session 2500");
        Assert.NotSame(far, now);
        Assert.Same(now.SwitchCommand, focused.Command);
        Assert.True(InView(app, focused));
    }

    [AvaloniaFact]
    public async Task The_scroll_position_survives_a_refresh()
    {
        var (app, screen) = await OpenAsync();
        using var _ = app;
        var area = app.Find<ScrollViewer>("CardsRegion");
        ScrollToCard(app, screen, "Session 1500");
        var before = area.Offset.Y;
        Assert.True(before > 1000, "the test needs to start well down the list");

        await screen.ReloadAsync();
        Layout(app);

        Assert.InRange(area.Offset.Y, before - 1, before + 1);
    }

    [AvaloniaFact]
    public async Task A_search_keeps_the_controls_drawn_for_the_lines_it_leaves_as_they_were()
    {
        var (app, screen) = await OpenAsync();
        using var _ = app;
        var list = app.Find<ItemsControl>("GroupList");
        var before = DrawnLines(screen, list);

        screen.Search.Text = "session 000";
        app.Clock.Advance(HistoryScreenViewModel.TypingPause);
        Layout(app);

        var after = DrawnLines(screen, list);
        var kept = after.Keys.Intersect(before.Keys).ToList();
        Assert.True(kept.Count >= 2, $"the test needs lines that stay; {kept.Count} did");
        Assert.All(kept, line => Assert.Same(before[line], after[line]));
    }

    // The control drawn for each line of the cards list, named by what the line holds.
    private static Dictionary<string, Control> DrawnLines(HistoryScreenViewModel screen, ItemsControl list) =>
        list.ItemsPanelRoot!.Children.Cast<Control>().ToDictionary(
            container => screen.CardEntries[list.IndexFromContainer(container)] switch
            {
                CardGroupHeadingViewModel heading => "heading:" + heading.Heading,
                CardRowViewModel row => "row:" + row.Cards[0].FocusKey,
                _ => throw new InvalidOperationException("An unknown line."),
            });

    [AvaloniaFact]
    public async Task Each_card_tells_assistive_technology_its_place_in_the_whole_list_and_the_group_it_is_in()
    {
        var (app, screen) = await OpenAsync();
        using var _ = app;
        ScrollToCard(app, screen, "Session 2500");
        var cards = screen.Groups.SelectMany(group => group.Cards).ToList();
        var index = cards.FindIndex(card => card.Name == "Session 2500");
        var group = screen.Groups.Single(candidate => candidate.Cards.Any(card => card.Name == "Session 2500"));

        var item = app.Find<ItemsControl>("GroupList").GetVisualDescendants().OfType<Border>()
            .First(border => border.Classes.Contains("card") && ((PointCardViewModel)border.DataContext!).Name == "Session 2500");
        var peer = Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(item);

        // The peer has no position or size to ask; the Windows provider reads them from these properties, and the peer gives the name.
        Assert.Equal(index + 1, Avalonia.Automation.AutomationProperties.GetPositionInSet(item));
        Assert.Equal(cards.Count, Avalonia.Automation.AutomationProperties.GetSizeOfSet(item));
        Assert.StartsWith($"{group.Heading}: Session 2500", peer.GetName(), StringComparison.Ordinal);
        Assert.True(index > 2000, "the card should be far enough down for a count that restarts to differ");
    }

    [AvaloniaFact]
    public async Task The_group_headings_are_headings_to_assistive_technology()
    {
        var (app, screen) = await OpenAsync();
        using var _ = app;

        var heading = app.Find<ItemsControl>("GroupList").GetVisualDescendants().OfType<TextBlock>()
            .First(text => text.DataContext is CardGroupHeadingViewModel);

        var peer = Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(heading);
        Assert.Equal(2, peer.GetHeadingLevel());
        Assert.Contains(heading.Text, screen.Groups.Select(group => group.Heading), StringComparer.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public async Task The_saves_and_the_timeline_tell_assistive_technology_their_places_too()
    {
        var (app, screen) = await OpenAsync(list: true);
        using var _ = app;

        var save = app.Find<ItemsControl>("SavesList").GetVisualDescendants().OfType<Border>().First(border => border.Classes.Contains("save-row"));
        var entry = app.Find<ItemsControl>("TimelineList").GetVisualDescendants().OfType<Grid>()
            .First(grid => Avalonia.Automation.AutomationProperties.GetControlTypeOverride(grid) == Avalonia.Automation.Peers.AutomationControlType.ListItem);

        Assert.Equal(1, Avalonia.Automation.AutomationProperties.GetPositionInSet(save));
        Assert.Equal(screen.Saves.Count, Avalonia.Automation.AutomationProperties.GetSizeOfSet(save));
        Assert.Equal(1, Avalonia.Automation.AutomationProperties.GetPositionInSet(entry));
        Assert.Equal(screen.Timeline.Count, Avalonia.Automation.AutomationProperties.GetSizeOfSet(entry));
    }

    [AvaloniaTheory]
    [InlineData(1040, 3)]
    [InlineData(700, 2)]
    [InlineData(440, 1)]
    public async Task The_cards_are_laid_out_in_as_many_columns_as_the_width_allows_and_stay_at_least_210_px_wide(double width, int columns)
    {
        using var app = await ComposedApp.StartWithViewAsync(ManySnapshots(), width: width, visitScreens: false);
        var screen = app.Show<HistoryScreenViewModel>(NavDestination.History);
        Layout(app);

        var cards = app.Find<ItemsControl>("GroupList").GetVisualDescendants().OfType<Border>().Where(border => border.Classes.Contains("card")).ToList();

        Assert.Equal(columns, screen.CardColumns);
        Assert.All(cards, card => Assert.True(card.Bounds.Width + 14 >= 209.5, $"a card is {card.Bounds.Width} px wide"));
        Assert.Equal(columns, cards.Select(card => Math.Round(card.TranslatePoint(default, app.Find<ScrollViewer>("CardsRegion"))!.Value.Y)).GroupBy(top => top).Max(row => row.Count()));
    }
}
