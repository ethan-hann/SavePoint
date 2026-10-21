using Avalonia.Headless.XUnit;
using Savepoint.App.Features.History;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.History;

// Writes PNGs to render-output/history/ (gitignored) for side-by-side review against 2h and 2j at 1040x660.
public class HistoryRenderTests
{
    private static async Task<ScreenHarness> StartAsync(ThemePreference theme = ThemePreference.Dark, FakeBackend? backend = null)
    {
        backend ??= new FakeBackend(ScenarioBuilder.Mockup().WithSettings(MockupScenario.Settings with { Theme = theme }));
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

        AssertRendered(harness.Render("2h-history-list-dark"));
    }

    [AvaloniaFact]
    public async Task Cards_dark_render_to_a_png()
    {
        using var harness = await StartAsync();

        AssertRendered(harness.Render("2j-history-cards-dark"));
    }

    [AvaloniaFact]
    public async Task List_light_renders_to_a_png()
    {
        using var harness = await StartAsync(ThemePreference.Light);
        ChooseList(harness);

        AssertRendered(harness.Render("2h-history-list-light"));
    }

    [AvaloniaFact]
    public async Task Cards_light_render_to_a_png()
    {
        using var harness = await StartAsync(ThemePreference.Light);

        AssertRendered(harness.Render("2j-history-cards-light"));
    }

    [AvaloniaFact]
    public async Task Empty_dark_renders_to_a_png()
    {
        using var harness = await StartAsync();
        harness.Shell.SelectGame(MockupScenario.SatisfactoryId);

        AssertRendered(harness.Render("history-empty-dark"));
    }

    [AvaloniaFact]
    public async Task Failed_read_dark_renders_to_a_png()
    {
        var backend = new FakeBackend(ScenarioBuilder.Mockup().WithSettings(MockupScenario.Settings));
        backend.FailNext<IHistoryApi>(nameof(IHistoryApi.GetAsync), ErrorInfo.From(new InvalidOperationException("The history could not be read.")));
        using var harness = await StartAsync(backend: backend);

        AssertRendered(harness.Render("history-failed-dark"));
    }

    [AvaloniaFact]
    public async Task Narrow_cards_dark_render_to_a_png()
    {
        var backend = new FakeBackend(ScenarioBuilder.Mockup().WithSettings(MockupScenario.Settings));
        var clock = backend.Clock;
        using var harness = await ScreenHarness.StartAsync("history", context => [new HistoryScreenFactory(context, clock)], NavDestination.History, backend, ThemePreference.Dark, width: 760);

        AssertRendered(harness.Render("2j-history-cards-narrow-dark"));
    }
}
