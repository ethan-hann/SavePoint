using Avalonia.Headless.XUnit;
using Savepoint.App.Features.Settings;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.EndToEnd;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Help;

// Writes PNGs to render-output/help/ (gitignored) for review: the page whole, in both themes, and the ways into it.
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
        using var app = await ComposedApp.StartWithViewAsync(new FakeBackend(ScenarioBuilder.Empty().WithSettings(MockupScenario.Settings)));

        AssertRendered(app.Render("help", "first-run-link"));
    }

    [AvaloniaFact]
    public async Task Settings_with_its_Help_section_renders_to_a_png()
    {
        using var app = await ComposedApp.StartWithViewAsync(height: 760);
        app.Show<SettingsScreenViewModel>(NavDestination.Settings);

        AssertRendered(app.Render("help", "settings-help"));
    }
}
