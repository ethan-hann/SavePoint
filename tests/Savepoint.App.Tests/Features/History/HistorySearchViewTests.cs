using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Savepoint.App.Features.Common.Search;
using Savepoint.App.Features.History;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.EndToEnd;
using Savepoint.App.Tests.Theme;
using Savepoint.Core.Api;

namespace Savepoint.App.Tests.Features.History;

public class HistorySearchViewTests
{
    private static TextBox Field(ComposedApp app) => app.Find<SearchBoxView>("HistorySearchBox").GetVisualDescendants().OfType<TextBox>().Single();

    private static async Task<ComposedApp> StartAsync(FakeBackend? backend = null, bool list = false)
    {
        var app = await ComposedApp.StartWithViewAsync(backend);
        app.Shell.Navigate(NavDestination.History);
        if (list)
        {
            app.History.SelectedViewOption = app.History.ViewOptions[1];
        }

        app.Settle();
        return app;
    }

    private static List<string?> CardNames(ComposedApp app) =>
        [.. app.Find<ItemsControl>("GroupList").GetVisualDescendants().OfType<Border>().Where(border => border.Classes.Contains("card")).Select(border => AutomationAudit.NameOf(border))];

    // Only the cards in view are drawn: they are the first of the snapshots the screen holds, and the screen holds `total`.
    private static void AssertTopOfTheListIsDrawn(ComposedApp app, int total)
    {
        var all = app.History.Groups.SelectMany(group => group.Cards).Select(card => card.ItemAutomationName).ToList();
        var drawn = CardNames(app);

        Assert.Equal(total, all.Count);
        Assert.NotEmpty(drawn);
        Assert.Equal(all.Take(drawn.Count).Order(), drawn.Order());
    }

    private static void Type(ComposedApp app, string text)
    {
        app.Window.KeyTextInput(text);
        app.Settle();
    }

    [AvaloniaFact]
    public async Task The_search_row_sits_under_the_header_with_names_a_screen_reader_can_say()
    {
        using var app = await StartAsync();

        var field = Field(app);

        Assert.True(field.IsEffectivelyVisible);
        Assert.Equal("Search snapshots", AutomationAudit.NameOf(field));
        Assert.Equal("Kind of snapshot", AutomationAudit.NameOf(app.Find<ListBox>("KindToggle")));
        Assert.Empty(AutomationAudit.Problems(app.View!));
    }

    [AvaloniaFact]
    public async Task Ctrl_F_on_History_moves_to_the_snapshot_search()
    {
        using var app = await StartAsync();

        app.Press(Key.F, PhysicalKey.F, RawInputModifiers.Control);

        Assert.Same(Field(app), app.Focused);
    }

    [AvaloniaFact]
    public async Task Typing_narrows_the_cards_and_Escape_brings_them_back()
    {
        using var app = await StartAsync();
        Field(app).Focus();

        Type(app, "blackreach");
        Assert.Single(CardNames(app));

        app.Press(Key.Escape, PhysicalKey.Escape);
        AssertTopOfTheListIsDrawn(app, 8);
        Assert.Same(Field(app), app.Focused);
    }

    [AvaloniaFact]
    public async Task Typing_narrows_the_list_view_too()
    {
        using var app = await StartAsync(list: true);
        Field(app).Focus();

        Type(app, "2026-06");

        Assert.Single(
            app.Find<ItemsControl>("TimelineList").GetVisualDescendants().OfType<Button>(),
            button => AutomationAudit.NameOf(button) == "Go back and play: Vanilla 1.6.1170");
        Assert.DoesNotContain(app.Find<ItemsControl>("SavesList").GetVisualDescendants().OfType<Border>(), border => border.Classes.Contains("save-row"));
    }

    [AvaloniaFact]
    public async Task The_kind_group_is_a_named_group_of_radio_buttons_that_narrows_the_snapshots()
    {
        using var app = await StartAsync();
        var group = ControlAutomationPeer.CreatePeerForElement(app.Find<ListBox>("KindToggle"))!;
        Assert.Equal(AutomationControlType.Group, group.GetAutomationControlType());
        var options = group.GetChildren().SelectMany(Flatten).Where(peer => peer is ISelectionItemProvider).ToList();
        Assert.Equal(["All", "Saves", "Game and mods"], options.Select(option => option.GetName()));
        Assert.All(options, option => Assert.Equal(AutomationControlType.RadioButton, option.GetAutomationControlType()));

        ((ISelectionItemProvider)options[2]).Select();
        app.Settle();

        Assert.Equal(4, CardNames(app).Count);
    }

    [AvaloniaFact]
    public async Task A_search_nothing_matches_says_so_with_a_way_to_clear_it_and_keeps_the_search_row()
    {
        using var app = await StartAsync();
        Field(app).Focus();
        Type(app, "zzz");
        var state = app.Find<StackPanel>("SearchEmptyState");

        Assert.True(state.IsEffectivelyVisible);
        Assert.True(Field(app).IsEffectivelyVisible);
        Assert.False(app.Find<Panel>("PointsRegion").IsEffectivelyVisible);
        Assert.Equal("No snapshots match.", state.GetVisualDescendants().OfType<TextBlock>().First().Text);

        new ButtonAutomationPeer(state.GetVisualDescendants().OfType<Button>().Single()).Invoke();
        app.Settle();

        Assert.False(state.IsEffectivelyVisible);
        AssertTopOfTheListIsDrawn(app, 8);
    }

    // Tab from where focus is until it lands on the control, or false when it comes round without.
    private static bool TabReaches(ComposedApp app, Control target, int limit = 80)
    {
        for (var i = 0; i < limit; i++)
        {
            app.Press(Key.Tab, PhysicalKey.Tab);
            if (ReferenceEquals(app.Focused, target))
            {
                return true;
            }
        }

        return false;
    }

    // A game with one snapshot of the given kind and one recently removed snapshot that holds the game's version and mods, so the
    // Saves choice alone empties the removed section, and empties the list too when the snapshot holds no save.
    private static async Task<ComposedApp> StartWithKindOnlyEmptyAsync(HistoryPointKind pointKind)
    {
        var scenario = ScenarioBuilder.Sample()
            .WithGame(new Savepoint.Core.Catalog.GameDefinition(Id: "bare", Name: "Bare", Install: new Savepoint.Core.Catalog.InstallLocation(@"D:\Bare")))
            .WithHistoryPoint("bare", new HistoryPoint(new HistoryPointId("pt-only"), pointKind, "Only one", ScenarioBuilder.DefaultNow, SampleScenario.ModdedId, "Modded playthrough - 7-2-26", new InstallState("1.6.640", 212), null, null, false, false))
            .WithRemovedSnapshot("bare", RemovedSnapshotSamples.FromDeletedProfile);
        var app = await StartAsync(new FakeBackend(scenario));
        app.Shell.SelectGame("bare");
        await app.History.ReloadAsync();
        app.Settle();
        return app;
    }

    [AvaloniaFact]
    public async Task When_only_the_kind_choice_empties_the_list_the_clear_button_is_on_and_Tab_reaches_it_and_Enter_lists_everything()
    {
        using var app = await StartWithKindOnlyEmptyAsync(HistoryPointKind.VersionAndMods);
        app.History.SelectedKindOption = app.History.KindOptions[1];
        app.Settle();
        var button = app.Find<Button>("SearchEmptyClearButton");
        Assert.True(button.IsEffectivelyVisible);
        Field(app).Focus();

        Assert.True(button.IsEnabled);
        Assert.True(button.Focusable && button.IsTabStop);
        Assert.True(TabReaches(app, button));

        app.Press(Key.Enter, PhysicalKey.Enter);
        app.Settle();

        Assert.Equal(HistoryKindFilter.All, app.History.SelectedKindOption?.Filter);
        Assert.False(app.Find<StackPanel>("SearchEmptyState").IsEffectivelyVisible);
        Assert.Single(CardNames(app));
        Assert.Same(Field(app), app.Focused);
    }

    [AvaloniaFact]
    public async Task When_only_the_kind_choice_empties_the_removed_section_its_clear_button_is_on_and_Tab_reaches_it_and_Enter_lists_everything()
    {
        using var app = await StartWithKindOnlyEmptyAsync(HistoryPointKind.SaveOnly);
        app.History.IsRemovedOpen = true;
        app.History.SelectedKindOption = app.History.KindOptions[1];
        app.Settle();
        var button = app.Find<Button>("RemovedClearSearchButton");
        Assert.True(button.IsEffectivelyVisible);
        Field(app).Focus();

        Assert.True(button.IsEnabled);
        Assert.True(button.Focusable && button.IsTabStop);
        Assert.True(TabReaches(app, button));

        app.Press(Key.Enter, PhysicalKey.Enter);
        app.Settle();

        Assert.Equal(HistoryKindFilter.All, app.History.SelectedKindOption?.Filter);
        Assert.False(app.Find<StackPanel>("RemovedNoMatches").IsEffectivelyVisible);
        Assert.Single(app.History.Removed);
        Assert.Same(Field(app), app.Focused);
    }

    [AvaloniaFact]
    public async Task The_removed_section_offers_a_named_button_to_clear_the_search_that_Tab_reaches()
    {
        using var app = await StartAsync(new FakeBackend(RemovedSnapshotSamples.Scenario()));
        app.History.IsRemovedOpen = true;
        app.Settle();
        Field(app).Focus();
        Type(app, "zzz");
        var button = app.Find<Button>("RemovedClearSearchButton");

        Assert.True(button.IsEffectivelyVisible);
        Assert.Equal("Clear search", AutomationAudit.NameOf(button));
        Assert.True(button.Focusable && button.IsTabStop);
        Assert.True(TabReaches(app, button));
        Assert.Empty(AutomationAudit.Problems(app.View!));
    }

    [AvaloniaFact]
    public async Task Clearing_the_search_from_the_removed_sections_button_with_the_keyboard_puts_focus_back_in_the_search_box()
    {
        using var app = await StartAsync(new FakeBackend(RemovedSnapshotSamples.Scenario()));
        app.History.IsRemovedOpen = true;
        app.Settle();
        Field(app).Focus();
        Type(app, "zzz");
        var button = app.Find<Button>("RemovedClearSearchButton");
        button.Focus();

        app.Press(Key.Enter, PhysicalKey.Enter);
        app.Settle();

        Assert.Equal(string.Empty, Field(app).Text);
        Assert.Same(Field(app), app.Focused);
    }

    [AvaloniaFact]
    public async Task Clearing_the_search_from_the_no_matches_message_with_the_keyboard_puts_focus_back_in_the_search_box()
    {
        using var app = await StartAsync();
        Field(app).Focus();
        Type(app, "zzz");
        var button = app.Find<Button>("SearchEmptyClearButton");
        Assert.True(TabReaches(app, button));

        app.Press(Key.Enter, PhysicalKey.Enter);
        app.Settle();

        Assert.Equal(string.Empty, Field(app).Text);
        Assert.Same(Field(app), app.Focused);
    }

    [AvaloniaFact]
    public async Task Recently_removed_snapshots_are_narrowed_and_say_so_when_none_match()
    {
        using var app = await StartAsync(new FakeBackend(RemovedSnapshotSamples.Scenario()));
        app.History.IsRemovedOpen = true;
        app.Settle();
        Field(app).Focus();

        Type(app, "blackreach");
        Assert.Equal(1, app.Find<ItemsControl>("RemovedList").GetVisualDescendants().OfType<Border>().Count(border => border.Classes.Contains("save-row")));
        Assert.Equal("Recently removed (1 of 2)", app.Find<TextBlock>("RemovedHeadingText").Text);

        Type(app, "x");
        Assert.True(app.Find<TextBlock>("RemovedNoMatchesLine").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task Writes_the_history_search_renders_on_request()
    {
        RenderOutput.SkipUnlessRequested();
        using var cards = await StartAsync(new FakeBackend(RemovedSnapshotSamples.Scenario()));
        cards.History.IsRemovedOpen = true;
        cards.History.Search.Text = "survival";
        cards.Render("history", "history-search-cards");

        cards.History.Search.Text = "blackreach";
        cards.Render("history", "history-search-removed");

        cards.History.Search.Text = "zzz";
        cards.Render("history", "history-search-empty");

        using var list = await StartAsync(list: true);
        list.History.Search.Text = "2026-07";
        list.Render("history", "history-search-list");
    }

    private static IEnumerable<AutomationPeer> Flatten(AutomationPeer peer) => new[] { peer }.Concat(peer.GetChildren().SelectMany(Flatten));
}
