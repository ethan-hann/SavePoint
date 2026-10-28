using Avalonia.Headless.XUnit;
using Savepoint.App.Features.Settings;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.EndToEnd;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Help;

// Writes PNGs to render-output/help/ (gitignored) to compare with the previous render of the page after a change: the page whole, in both themes, and the ways into it.
// Nothing here asserts on a picture, so the renders are only drawn on request: SAVEPOINT_RENDER=1.
public class HelpRenderTests
{
    private static readonly string[] _openNotes =
    [
        "history-help-open",
        "settings-help-open",
        "launch-dialog-help-open",
        "post-play-replace-help-open",
        "post-play-from-older-snapshot",
        "launch-discard-first-time",
    ];

    private static readonly (ShellLayout Layout, ThemePreference Theme, int Width, int Height)[] _tours =
    [
        (ShellLayout.Sidebar, ThemePreference.Dark, 1040, 660),
        (ShellLayout.Sidebar, ThemePreference.Light, 1040, 660),
        (ShellLayout.Library, ThemePreference.Dark, 1040, 660),
        (ShellLayout.Library, ThemePreference.Light, 1040, 660),
        (ShellLayout.Sidebar, ThemePreference.Dark, 910, 432),
        (ShellLayout.Library, ThemePreference.Dark, 640, 420),
    ];

    [AvaloniaFact]
    public async Task Writes_every_Help_render_on_request()
    {
        RenderOutput.SkipUnlessRequested();

        await TheWholePage(ThemePreference.Dark, "how-it-works-dark");
        await TheWholePage(ThemePreference.Light, "how-it-works-light");
        await TheTopOfThePageAtTheSmallestWindow();
        await TheFirstRunScreenWithItsLink();
        foreach (var state in _openNotes)
        {
            await AnOpenExplanationOrDecisionNote(state);
        }

        await AnOpenExplanationInTheLightTheme();
        foreach (var (layout, theme, width, height) in _tours)
        {
            await EachStepOfTheTour(layout, theme, width, height);
        }

        await SettingsWithItsHelpSection();
    }

    private static async Task TheWholePage(ThemePreference theme, string name)
    {
        // Tall enough to show every topic at once, which no real window needs: the page scrolls.
        using var app = await ComposedApp.StartWithViewAsync(theme: theme, width: 1040, height: 3400);
        app.Shell.ShowHelp();

        app.Render("help", name);
    }

    private static async Task TheTopOfThePageAtTheSmallestWindow()
    {
        using var app = await ComposedApp.StartWithViewAsync(width: 640, height: 420);
        app.Shell.ShowHelp();

        app.Render("help", "how-it-works-640x420");
    }

    private static async Task TheFirstRunScreenWithItsLink()
    {
        using var app = await ComposedApp.StartWithViewAsync(new FakeBackend(ScenarioBuilder.Empty().WithSettings(SampleScenario.Settings)));

        app.Render("help", "first-run-link");
    }

    private static async Task AnOpenExplanationOrDecisionNote(string state)
    {
        using var app = await AppStates.OpenAsync(state);

        app.Render("help", state);
    }

    private static async Task AnOpenExplanationInTheLightTheme()
    {
        using var app = await ComposedApp.StartWithViewAsync(theme: ThemePreference.Light);
        app.Show<Savepoint.App.Features.History.HistoryScreenViewModel>(NavDestination.History).SnapshotHelp.IsOpen = true;

        app.Render("help", "history-help-open-light");
    }

    private static async Task EachStepOfTheTour(ShellLayout layout, ThemePreference theme, int width, int height)
    {
        var settings = SampleScenario.Settings with { TourSeen = false, Layout = layout, Theme = theme };
        using var app = await ComposedApp.StartWithViewAsync(new FakeBackend(ScenarioBuilder.Sample().WithSettings(settings)), theme: theme, width: width, height: height);

        for (var step = 1; step <= 4; step++)
        {
            app.Render("help", $"tour-{layout.ToString().ToLowerInvariant()}-{theme.ToString().ToLowerInvariant()}-{width}x{height}-step{step}");
            app.Shell.Tour.NextCommand.Execute(null);
        }
    }

    private static async Task SettingsWithItsHelpSection()
    {
        using var app = await ComposedApp.StartWithViewAsync(height: 760);
        app.Show<SettingsScreenViewModel>(NavDestination.Settings);

        app.Render("help", "settings-help");
    }
}
