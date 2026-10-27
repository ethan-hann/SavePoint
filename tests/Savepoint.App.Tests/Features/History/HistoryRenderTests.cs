using Avalonia.Headless.XUnit;
using Savepoint.App.Features.History;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.History;

// Writes PNGs to render-output/history/ (gitignored) at 1040x660 to compare with the previous render of the History list and cards after a change.
public class HistoryRenderTests
{
    private static async Task<ScreenHarness> StartAsync(ThemePreference theme = ThemePreference.Dark, FakeBackend? backend = null)
    {
        backend ??= new FakeBackend(ScenarioBuilder.Sample().WithSettings(SampleScenario.Settings with { Theme = theme }));
        var clock = backend.Clock;
        return await ScreenHarness.StartAsync("history", context => [new HistoryScreenFactory(context, clock)], NavDestination.History, backend, theme);
    }

    private static void AssertRendered(string path) =>
        Assert.True(new FileInfo(path).Length > 10_000, "The render looks empty.");

    private static void ChooseList(ScreenHarness harness)
    {
        var screen = (HistoryScreenViewModel)harness.Shell.CurrentScreen!;
        screen.SelectedViewOption = screen.ViewOptions[1];
    }

    [AvaloniaFact]
    public async Task List_dark_renders_to_a_png()
    {
        using var harness = await StartAsync();
        ChooseList(harness);

        AssertRendered(harness.Render("history-list-dark"));
    }

    [AvaloniaFact]
    public async Task Cards_dark_render_to_a_png()
    {
        using var harness = await StartAsync();

        AssertRendered(harness.Render("history-cards-dark"));
    }

    [AvaloniaFact]
    public async Task List_light_renders_to_a_png()
    {
        using var harness = await StartAsync(ThemePreference.Light);
        ChooseList(harness);

        AssertRendered(harness.Render("history-list-light"));
    }

    [AvaloniaFact]
    public async Task Cards_light_render_to_a_png()
    {
        using var harness = await StartAsync(ThemePreference.Light);

        AssertRendered(harness.Render("history-cards-light"));
    }

    [AvaloniaFact]
    public async Task Empty_dark_renders_to_a_png()
    {
        using var harness = await StartAsync();
        harness.Shell.SelectGame(SampleScenario.SatisfactoryId);

        AssertRendered(harness.Render("history-empty-dark"));
    }

    [AvaloniaFact]
    public async Task Failed_read_dark_renders_to_a_png()
    {
        var backend = new FakeBackend(ScenarioBuilder.Sample().WithSettings(SampleScenario.Settings));
        backend.FailNext<IHistoryApi>(nameof(IHistoryApi.GetAsync), ErrorInfo.From(new InvalidOperationException("The history could not be read.")));
        using var harness = await StartAsync(backend: backend);

        AssertRendered(harness.Render("history-failed-dark"));
    }

    private static async Task<ScreenHarness> RemovedAsync(ThemePreference theme, bool open, bool list = false, double width = 1040, double height = 660, bool realDialogs = false)
    {
        var backend = new FakeBackend(RemovedSnapshotSamples.Scenario().WithSettings(SampleScenario.Settings with { Theme = theme }));
        var clock = backend.Clock;
        var harness = await ScreenHarness.StartAsync("history", context => [new HistoryScreenFactory(context, clock)], NavDestination.History, backend, theme, width, height, realDialogs);
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
    public async Task Recently_removed_closed_cards_dark_render_to_a_png()
    {
        using var harness = await RemovedAsync(ThemePreference.Dark, open: false);

        AssertRendered(harness.Render("history-removed-closed-dark"));
    }

    [AvaloniaFact]
    public async Task Recently_removed_open_cards_dark_render_to_a_png()
    {
        using var harness = await RemovedAsync(ThemePreference.Dark, open: true);

        AssertRendered(harness.Render("history-removed-open-dark"));
    }

    [AvaloniaFact]
    public async Task Recently_removed_open_cards_light_render_to_a_png()
    {
        using var harness = await RemovedAsync(ThemePreference.Light, open: true);

        AssertRendered(harness.Render("history-removed-open-light"));
    }

    [AvaloniaFact]
    public async Task Recently_removed_open_list_dark_render_to_a_png()
    {
        using var harness = await RemovedAsync(ThemePreference.Dark, open: true, list: true);

        AssertRendered(harness.Render("history-removed-open-list-dark"));
    }

    [AvaloniaFact]
    public async Task Recently_removed_open_at_the_smallest_supported_window_renders_to_a_png()
    {
        using var harness = await RemovedAsync(ThemePreference.Dark, open: true, width: 960, height: 460);

        AssertRendered(harness.Render("history-removed-open-small-dark"));
    }

    [AvaloniaFact]
    public async Task The_bring_back_dialog_renders_to_a_png()
    {
        using var harness = await RemovedAsync(ThemePreference.Dark, open: true, realDialogs: true);
        var screen = (HistoryScreenViewModel)harness.Shell.CurrentScreen!;

        screen.Removed[0].BringBackCommand.Execute(null);
        harness.Settle();

        AssertRendered(harness.Render("history-removed-bring-back-dialog-dark"));
    }

    [AvaloniaFact]
    public async Task Narrow_cards_dark_render_to_a_png()
    {
        var backend = new FakeBackend(ScenarioBuilder.Sample().WithSettings(SampleScenario.Settings));
        var clock = backend.Clock;
        using var harness = await ScreenHarness.StartAsync("history", context => [new HistoryScreenFactory(context, clock)], NavDestination.History, backend, ThemePreference.Dark, width: 760);

        AssertRendered(harness.Render("history-cards-narrow-dark"));
    }
}
