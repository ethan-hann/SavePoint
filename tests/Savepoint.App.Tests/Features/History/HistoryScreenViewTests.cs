using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Savepoint.App.Composition;
using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Features.History;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Theme;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.History;

public class HistoryScreenViewTests
{
    private static Task<ScreenHarness> StartAsync(FakeBackend? backend = null, ThemePreference theme = ThemePreference.Dark, double width = 1040, double height = 660)
    {
        backend ??= new FakeBackend(ScenarioBuilder.Mockup().WithSettings(MockupScenario.Settings with { Theme = theme }));
        var clock = backend.Clock;
        return ScreenHarness.StartAsync("history", context => [new HistoryScreenFactory(context, clock)], NavDestination.History, backend, theme, width, height);
    }

    private static HistoryScreenViewModel Screen(ScreenHarness harness) => (HistoryScreenViewModel)harness.Shell.CurrentScreen!;

    private static Button ButtonNamed(ScreenHarness harness, string name) =>
        harness.All<Button>().Single(button => AutomationProperties.GetName(button) == name);

    [AvaloniaFact]
    public async Task The_header_shows_the_title_the_game_and_the_point_count()
    {
        using var harness = await StartAsync();

        var texts = harness.VisibleTexts().ToList();

        Assert.Contains("Points you can go back to", texts);
        Assert.Contains("Skyrim Special Edition · 8 points kept", texts);
        Assert.Contains("All profiles", texts);
        Assert.Contains("Cards", texts);
        Assert.Contains("List", texts);
    }

    [AvaloniaFact]
    public async Task Cards_show_by_default_in_two_groups_with_a_button_on_every_card_but_the_loaded_one()
    {
        using var harness = await StartAsync();

        var texts = harness.VisibleTexts().ToList();

        Assert.True(harness.Find<ScrollViewer>("CardsRegion").IsEffectivelyVisible);
        Assert.False(harness.Find<Grid>("ListRegion").IsEffectivelyVisible);
        Assert.Contains("LAST 7 DAYS", texts);
        Assert.Contains("EARLIER", texts);
        Assert.Contains("LOADED NOW", texts);
        Assert.Contains("Save only · 1.6.640 with 212 mod files", texts);
        Assert.Equal(7, harness.All<Button>().Count(button => button.IsEffectivelyVisible && (button.Content as string) == "Go back to this"));
    }

    [AvaloniaFact]
    public async Task Choosing_list_shows_the_two_columns_with_their_headings_and_hides_the_cards()
    {
        using var harness = await StartAsync();

        Screen(harness).SelectedViewOption = Screen(harness).ViewOptions[1];
        harness.Settle();

        var texts = harness.VisibleTexts().ToList();
        Assert.True(harness.Find<Grid>("ListRegion").IsEffectivelyVisible);
        Assert.False(harness.Find<ScrollViewer>("CardsRegion").IsEffectivelyVisible);
        Assert.Contains("VERSIONS & MODS", texts);
        Assert.Contains("SAVES", texts);
        Assert.Contains("14 mod files updated", texts);
        Assert.Contains("LOADED", texts);
        Assert.Equal(4, harness.All<Button>().Count(button => button.IsEffectivelyVisible && (button.Content as string) == "Load"));
    }

    [AvaloniaFact]
    public async Task The_list_offers_go_back_on_every_version_and_mods_entry_that_is_not_in_use_named_after_it()
    {
        using var harness = await StartAsync();
        Screen(harness).SelectedViewOption = Screen(harness).ViewOptions[1];
        harness.Settle();

        var buttons = harness.Find<Grid>("ListRegion").GetVisualDescendants().OfType<Button>()
            .Where(button => button.IsEffectivelyVisible && (button.Content as string) == "Go back to this").ToList();

        var entries = Screen(harness).Timeline.Where(entry => !entry.IsInUse).ToList();
        Assert.NotEmpty(entries);
        Assert.Equal(entries.Count, buttons.Count);
        Assert.All(buttons, button => Assert.StartsWith("Go back to this: ", AutomationProperties.GetName(button), StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task The_toggle_in_the_header_drives_the_view_model()
    {
        using var harness = await StartAsync();

        harness.Find<ListBox>("ViewToggle").SelectedIndex = 1;
        harness.Settle();

        Assert.Equal(HistoryViewMode.List, Screen(harness).ViewMode);
    }

    [AvaloniaFact]
    public async Task A_cards_button_begins_the_launch_flow_for_its_point()
    {
        using var harness = await StartAsync();

        ButtonNamed(harness, "Go back to this: Cleared Blackreach").Command!.Execute(null);

        var begun = Assert.Single(harness.Flow.Begun);
        Assert.Equal(new LaunchTarget.Point(MockupScenario.ClearedBlackreach), begun.Target);
    }

    [AvaloniaFact]
    public async Task A_save_rows_button_begins_the_launch_flow_with_that_save()
    {
        using var harness = await StartAsync();
        Screen(harness).SelectedViewOption = Screen(harness).ViewOptions[1];
        harness.Settle();

        ButtonNamed(harness, "Load Deep cave run").Command!.Execute(null);

        var begun = Assert.Single(harness.Flow.Begun);
        Assert.Equal(new LaunchTarget.Save(MockupScenario.SurvivalId, MockupScenario.DeepCaveRun), begun.Target);
    }

    [AvaloniaFact]
    public async Task Buttons_go_dim_while_the_flow_has_a_session_for_the_game()
    {
        using var harness = await StartAsync();

        harness.Flow.SetActive(MockupScenario.SkyrimId, true);
        harness.Settle();

        Assert.False(ButtonNamed(harness, "Go back to this: Cleared Blackreach").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public async Task The_footer_shows_the_space_kept()
    {
        using var harness = await StartAsync();

        Assert.True(harness.Find<Border>("Footer").IsEffectivelyVisible);
        Assert.StartsWith("41.2 GB kept for this game.", harness.Find<TextBlock>("FooterText").Text);
    }

    [AvaloniaFact]
    public async Task Every_icon_only_button_has_a_name_and_the_toggle_and_filter_are_named()
    {
        using var harness = await StartAsync();

        Assert.Empty(AutomationAudit.IconButtonsWithoutNames(harness.View));
        Assert.Equal("View", AutomationProperties.GetName(harness.Find<ListBox>("ViewToggle")));
        Assert.Equal("Filter by profile: All profiles", AutomationProperties.GetName(harness.Find<Button>("FilterButton")));
    }

    [AvaloniaFact]
    public async Task A_game_without_points_shows_the_empty_state_and_no_filter_or_toggle()
    {
        using var harness = await StartAsync();

        harness.Shell.SelectGame(MockupScenario.SatisfactoryId);
        harness.Settle();

        Assert.True(harness.Find<StackPanel>("EmptyState").IsEffectivelyVisible);
        Assert.Contains("Nothing to go back to yet", harness.VisibleTexts());
        Assert.False(harness.Find<Button>("FilterButton").IsEffectivelyVisible);
        Assert.False(harness.Find<Panel>("PointsRegion").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task A_profile_without_points_keeps_the_filter_button_so_another_profile_can_be_picked()
    {
        var backend = new FakeBackend(ScenarioBuilder.Mockup()
            .WithProfile(new Profile("profile-empty", MockupScenario.SkyrimId, "Fresh profile", "profile/profile-empty")));
        using var harness = await StartAsync(backend);

        Screen(harness).SelectedFilter = Screen(harness).Filters.Single(filter => filter.ProfileId == "profile-empty");
        harness.Settle();

        Assert.True(harness.Find<StackPanel>("EmptyState").IsEffectivelyVisible);
        Assert.Contains("No points for this profile yet", harness.VisibleTexts());
        var filter = harness.Find<Button>("FilterButton");
        Assert.True(filter.IsEffectivelyVisible);
        Assert.Contains("Fresh profile", harness.VisibleTexts());
        Assert.False(harness.Find<ListBox>("ViewToggle").IsEffectivelyVisible);

        Assert.Equal(["All profiles", "Modded playthrough - 7-2-26", "Vanilla - latest update", "Survival test - 5-14-26", "Fresh profile"], Screen(harness).Filters.Select(entry => entry.Label));
        Screen(harness).SelectedFilter = Screen(harness).Filters[0];
        harness.Settle();

        Assert.True(harness.Find<Panel>("PointsRegion").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task A_failed_read_shows_the_error_card_with_try_again()
    {
        var backend = FakeBackend.Mockup();
        backend.FailNext<IHistoryApi>(nameof(IHistoryApi.GetAsync), ErrorInfo.From(new InvalidOperationException("No history.")));
        using var harness = await StartAsync(backend);

        Assert.True(harness.Find<ContentControl>("ErrorHost").IsEffectivelyVisible);
        Assert.Contains("Something went wrong", harness.VisibleTexts());
        Assert.Contains(harness.All<Button>(), button => button.IsEffectivelyVisible && (button.Content as string) == "Try again");
    }

    [Fact]
    public void The_composition_registers_the_screen_for_the_history_destination()
    {
        var backend = FakeBackend.Mockup();
        var services = new AppServices(backend.Games, backend.Profiles, backend.Launch, backend.Settings, backend.History, backend.Jobs, backend.AppStatus, backend.Artwork);
        var context = new FeatureContext(
            services,
            new FakeDialogs(),
            HistoryRig.FormatterFor(backend),
            new ToastHostViewModel(new ManualTimeProvider(), new ImmediateUiDispatcher()),
            new ImmediateUiDispatcher());

        var factory = FeatureComposition.HistoryScreen(context, null);

        Assert.NotNull(factory);
        Assert.Equal(NavDestination.History, factory.Destination);
    }

    [AvaloniaFact]
    public async Task The_screen_has_a_view_the_locator_finds()
    {
        using var harness = await StartAsync();

        Assert.NotNull(harness.All<TextBlock>().FirstOrDefault(block => block.Name == "TitleText"));
        Assert.IsType<HistoryScreenView>(harness.Find<ContentControl>("ContentRegion").GetVisualDescendants().OfType<HistoryScreenView>().First());
    }
}
