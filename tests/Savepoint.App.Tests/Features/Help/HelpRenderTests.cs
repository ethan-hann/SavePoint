using Avalonia.Headless.XUnit;
using Savepoint.App.Features.Settings;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.EndToEnd;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Help;

// Writes PNGs to render-output/help/ (gitignored) to compare with the previous render of the page after a change: the page whole, in both themes, and the ways into it.
public class HelpRenderTests
{
    private static void AssertRendered(string path) =>
        Assert.True(new FileInfo(path).Length > 10_000, "The render looks empty.");

    [AvaloniaTheory]
    [InlineData(ThemePreference.Dark, "how-it-works-dark")]
    [InlineData(ThemePreference.Light, "how-it-works-light")]
    public async Task The_whole_page_renders_to_a_png(ThemePreference theme, string name)
    {
        // Tall enough to show every topic at once, which no real window needs: the page scrolls.
        using var app = await ComposedApp.StartWithViewAsync(theme: theme, width: 1040, height: 3400);
        app.Shell.ShowHelp();

        AssertRendered(app.Render("help", name));
    }

    [AvaloniaFact]
    public async Task The_top_of_the_page_at_the_smallest_window_renders_to_a_png()
    {
        using var app = await ComposedApp.StartWithViewAsync(width: 640, height: 420);
        app.Shell.ShowHelp();

        AssertRendered(app.Render("help", "how-it-works-640x420"));
    }

    [AvaloniaFact]
    public async Task The_first_run_screen_with_its_link_renders_to_a_png()
    {
        using var app = await ComposedApp.StartWithViewAsync(new FakeBackend(ScenarioBuilder.Empty().WithSettings(SampleScenario.Settings)));

        AssertRendered(app.Render("help", "first-run-link"));
    }

    [AvaloniaTheory]
    [InlineData("history-help-open")]
    [InlineData("settings-help-open")]
    [InlineData("launch-dialog-help-open")]
    [InlineData("post-play-replace-help-open")]
    [InlineData("post-play-from-older-snapshot")]
    [InlineData("launch-discard-first-time")]
    public async Task An_open_explanation_or_decision_note_renders_to_a_png(string state)
    {
        using var app = await AppStates.OpenAsync(state);

        AssertRendered(app.Render("help", state));
    }

    [AvaloniaFact]
    public async Task An_open_explanation_in_the_light_theme_renders_to_a_png()
    {
        using var app = await ComposedApp.StartWithViewAsync(theme: ThemePreference.Light);
        app.Show<Savepoint.App.Features.History.HistoryScreenViewModel>(NavDestination.History).SnapshotHelp.IsOpen = true;

        AssertRendered(app.Render("help", "history-help-open-light"));
    }

    [AvaloniaTheory]
    [InlineData(ShellLayout.Sidebar, ThemePreference.Dark, 1040, 660)]
    [InlineData(ShellLayout.Sidebar, ThemePreference.Light, 1040, 660)]
    [InlineData(ShellLayout.Library, ThemePreference.Dark, 1040, 660)]
    [InlineData(ShellLayout.Library, ThemePreference.Light, 1040, 660)]
    [InlineData(ShellLayout.Sidebar, ThemePreference.Dark, 910, 432)]
    [InlineData(ShellLayout.Library, ThemePreference.Dark, 640, 420)]
    public async Task Each_step_of_the_tour_renders_to_a_png(ShellLayout layout, ThemePreference theme, int width, int height)
    {
        var settings = SampleScenario.Settings with { TourSeen = false, Layout = layout, Theme = theme };
        using var app = await ComposedApp.StartWithViewAsync(new FakeBackend(ScenarioBuilder.Sample().WithSettings(settings)), theme: theme, width: width, height: height);

        for (var step = 1; step <= 4; step++)
        {
            AssertRendered(app.Render("help", $"tour-{layout.ToString().ToLowerInvariant()}-{theme.ToString().ToLowerInvariant()}-{width}x{height}-step{step}"));
            app.Shell.Tour.NextCommand.Execute(null);
        }
    }

    [AvaloniaFact]
    public async Task Settings_with_its_Help_section_renders_to_a_png()
    {
        using var app = await ComposedApp.StartWithViewAsync(height: 760);
        app.Show<SettingsScreenViewModel>(NavDestination.Settings);

        AssertRendered(app.Render("help", "settings-help"));
    }
}
