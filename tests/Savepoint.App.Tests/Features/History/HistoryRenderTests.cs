using Avalonia.Headless.XUnit;
using Savepoint.App.Composition;
using Savepoint.App.Features.History;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.History;

// Writes PNGs to render-output/history/ (gitignored) at 1040x660 to compare with the previous render of the History list and cards after a change.
// Nothing here asserts on a picture, so the renders are only drawn on request: SAVEPOINT_RENDER=1.
public class HistoryRenderTests
{
    private static async Task<ScreenHarness> StartAsync(ThemePreference theme = ThemePreference.Dark, FakeBackend? backend = null)
    {
        backend ??= new FakeBackend(ScenarioBuilder.Sample().WithSettings(SampleScenario.Settings with { Theme = theme }));
        var clock = backend.Clock;
        return await ScreenHarness.StartAsync("history", context => [new HistoryScreenFactory(FeatureComposition.HistoryDependenciesOf(context, clock))], NavDestination.History, backend, theme);
    }

    private static void ChooseList(ScreenHarness harness)
    {
        var screen = (HistoryScreenViewModel)harness.Shell.CurrentScreen!;
        screen.SelectedViewOption = screen.ViewOptions[1];
    }

    private static async Task<ScreenHarness> RemovedAsync(ThemePreference theme, bool open, bool list = false, double width = 1040, double height = 660, bool realDialogs = false)
    {
        var backend = new FakeBackend(RemovedSnapshotSamples.Scenario().WithSettings(SampleScenario.Settings with { Theme = theme }));
        var clock = backend.Clock;
        var harness = await ScreenHarness.StartAsync("history", context => [new HistoryScreenFactory(FeatureComposition.HistoryDependenciesOf(context, clock))], NavDestination.History, backend, theme, width, height, realDialogs);
        var screen = (HistoryScreenViewModel)harness.Shell.CurrentScreen!;
        if (list)
        {
            screen.SelectedViewOption = screen.ViewOptions[1];
        }

        screen.IsRemovedOpen = open;
        harness.Settle();
        return harness;
    }

    [AvaloniaFact]
    public async Task Writes_every_History_render_on_request()
    {
        RenderOutput.SkipUnlessRequested();

        await ListDark();
        await CardsDark();
        await ListLight();
        await CardsLight();
        await EmptyDark();
        await FailedReadDark();
        await RecentlyRemovedClosedCardsDark();
        await RecentlyRemovedOpenCardsDark();
        await RecentlyRemovedOpenCardsLight();
        await RecentlyRemovedOpenListDark();
        await RecentlyRemovedOpenAtTheSmallestSupportedWindow();
        await TheBringBackDialog();
        await NarrowCardsDark();
    }

    private static async Task ListDark()
    {
        using var harness = await StartAsync();
        ChooseList(harness);

        harness.Render("history-list-dark");
    }

    private static async Task CardsDark()
    {
        using var harness = await StartAsync();

        harness.Render("history-cards-dark");
    }

    private static async Task ListLight()
    {
        using var harness = await StartAsync(ThemePreference.Light);
        ChooseList(harness);

        harness.Render("history-list-light");
    }

    private static async Task CardsLight()
    {
        using var harness = await StartAsync(ThemePreference.Light);

        harness.Render("history-cards-light");
    }

    private static async Task EmptyDark()
    {
        using var harness = await StartAsync();
        harness.Shell.SelectGame(SampleScenario.SatisfactoryId);

        harness.Render("history-empty-dark");
    }

    private static async Task FailedReadDark()
    {
        var backend = new FakeBackend(ScenarioBuilder.Sample().WithSettings(SampleScenario.Settings));
        backend.FailNext<IHistoryApi>(nameof(IHistoryApi.GetAsync), ErrorInfo.From(new InvalidOperationException("The history could not be read.")));
        using var harness = await StartAsync(backend: backend);

        harness.Render("history-failed-dark");
    }

    private static async Task RecentlyRemovedClosedCardsDark()
    {
        using var harness = await RemovedAsync(ThemePreference.Dark, open: false);

        harness.Render("history-removed-closed-dark");
    }

    private static async Task RecentlyRemovedOpenCardsDark()
    {
        using var harness = await RemovedAsync(ThemePreference.Dark, open: true);

        harness.Render("history-removed-open-dark");
    }

    private static async Task RecentlyRemovedOpenCardsLight()
    {
        using var harness = await RemovedAsync(ThemePreference.Light, open: true);

        harness.Render("history-removed-open-light");
    }

    private static async Task RecentlyRemovedOpenListDark()
    {
        using var harness = await RemovedAsync(ThemePreference.Dark, open: true, list: true);

        harness.Render("history-removed-open-list-dark");
    }

    private static async Task RecentlyRemovedOpenAtTheSmallestSupportedWindow()
    {
        using var harness = await RemovedAsync(ThemePreference.Dark, open: true, width: 960, height: 460);

        harness.Render("history-removed-open-small-dark");
    }

    private static async Task TheBringBackDialog()
    {
        using var harness = await RemovedAsync(ThemePreference.Dark, open: true, realDialogs: true);
        var screen = (HistoryScreenViewModel)harness.Shell.CurrentScreen!;

        screen.Removed[0].BringBackCommand.Execute(null);
        harness.Settle();

        harness.Render("history-removed-bring-back-dialog-dark");
    }

    private static async Task NarrowCardsDark()
    {
        var backend = new FakeBackend(ScenarioBuilder.Sample().WithSettings(SampleScenario.Settings));
        var clock = backend.Clock;
        using var harness = await ScreenHarness.StartAsync("history", context => [new HistoryScreenFactory(FeatureComposition.HistoryDependenciesOf(context, clock))], NavDestination.History, backend, ThemePreference.Dark, width: 760);

        harness.Render("history-cards-narrow-dark");
    }
}
