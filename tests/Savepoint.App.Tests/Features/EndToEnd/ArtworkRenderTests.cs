using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Savepoint.App.Features.Common.Art;
using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Localization;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.Common;
using Savepoint.App.Tests.Features.Editor;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.EndToEnd;

// Writes PNGs to render-output/artwork/ (gitignored) at 1040x660 for review: the library and sidebar layouts with real artwork
// next to a game that has none, and the editor's Artwork section in each state. A game with Steam art, one with its owner's own
// image and one with neither stand side by side.
public class ArtworkRenderTests
{
    private const string _area = "artwork";

    private static void AssertRendered(string path) =>
        Assert.True(new FileInfo(path).Length > 10_000, "The render looks empty.");

    private static FakeBackend BackendWithArt(ArtFixtures fixtures, ThemePreference theme, ShellLayout layout = ShellLayout.Sidebar)
    {
        var backend = new FakeBackend(ScenarioBuilder.Sample().WithSettings(SampleScenario.Settings with { Theme = theme, Layout = layout }));
        backend.Artwork.ScriptSteamArrives(
            SampleScenario.SkyrimId,
            fixtures.Scene("skyrim-wide.png", 460, 215, 0xFF2A4D69, 0xFF8AA2B5),
            fixtures.Scene("skyrim-square.png", 600, 900, 0xFF1B2A41, 0xFF6E8CA8));
        backend.Artwork.ScriptCustom(SampleScenario.SatisfactoryId, fixtures.Scene("satisfactory.png", 640, 360, 0xFFE07A3F, 0xFFF2C14E));
        return backend;
    }

    private static async Task<ComposedApp> StartAsync(FakeBackend backend, ThemePreference theme, ShellLayout layout)
    {
        var app = await ComposedApp.StartWithViewAsync(backend, theme, layout);
        var withArt = new[] { SampleScenario.SkyrimId, SampleScenario.SatisfactoryId };
        Assert.True(
            await UiWait.UntilAsync(() =>
            {
                // A tile in a hidden part of the layout (the header the library layout does not use) has no size and shows nothing.
                var shown = app.All<ArtTile>().Where(tile => tile.IsEffectivelyVisible && withArt.Contains(tile.Art?.GameId ?? string.Empty)).ToList();
                return shown.Count > 0 && shown.All(tile => tile.ShowsImage);
            }),
            "the artwork never appeared on its tiles");
        Assert.Contains(app.All<ArtTile>(), tile => tile.ShowsImage);
        Assert.Contains(app.All<ArtTile>(), tile => tile.IsEffectivelyVisible && tile.Art?.GameId == SampleScenario.CaretakerId && !tile.ShowsImage);
        return app;
    }

    [AvaloniaTheory]
    [InlineData(ThemePreference.Dark, "library-layout-artwork-dark")]
    [InlineData(ThemePreference.Light, "library-layout-artwork-light")]
    public async Task The_library_layout_with_artwork_renders_to_a_png(ThemePreference theme, string name)
    {
        using var fixtures = new ArtFixtures();
        using var app = await StartAsync(BackendWithArt(fixtures, theme, ShellLayout.Library), theme, ShellLayout.Library);

        AssertRendered(app.Render(_area, name));
    }

    [AvaloniaTheory]
    [InlineData(ThemePreference.Dark, "sidebar-layout-artwork-dark")]
    [InlineData(ThemePreference.Light, "sidebar-layout-artwork-light")]
    public async Task The_sidebar_layout_with_artwork_renders_to_a_png(ThemePreference theme, string name)
    {
        using var fixtures = new ArtFixtures();
        using var app = await StartAsync(BackendWithArt(fixtures, theme), theme, ShellLayout.Sidebar);

        AssertRendered(app.Render(_area, name));
    }

    [AvaloniaFact]
    public async Task The_history_header_shows_the_selected_games_image_and_renders_to_a_png()
    {
        using var fixtures = new ArtFixtures();
        using var app = await StartAsync(BackendWithArt(fixtures, ThemePreference.Dark), ThemePreference.Dark, ShellLayout.Sidebar);
        app.Shell.SelectGame(SampleScenario.SkyrimId);
        app.Shell.Navigate(Savepoint.App.Features.Shell.NavDestination.History);
        Assert.True(await UiWait.UntilAsync(() => app.All<ArtTile>().Any(tile => tile.Width == 34 && tile.ShowsImage)), "the history header never showed the image");

        AssertRendered(app.Render(_area, "history-header-artwork-dark"));
    }

    [AvaloniaFact]
    public async Task The_editor_shows_steam_artwork_with_its_app_id()
    {
        using var fixtures = new ArtFixtures();
        var backend = BackendWithArt(fixtures, ThemePreference.Dark);
        var game = FakeAssert.Ok(await backend.Games.ListAsync(TestContext.Current.CancellationToken)).Single(g => g.Id == SampleScenario.SkyrimId);
        await backend.Games.UpdateAsync(game with { SteamAppId = 489830 });
        using var harness = await EditorViewHarness.StartAsync(backend, height: 1000);
        harness.Edit(SampleScenario.SkyrimId);
        var card = harness.Find<ArtTile>("ArtworkCardPreview");
        var square = harness.Find<ArtTile>("ArtworkSquarePreview");
        Assert.True(await UiWait.UntilAsync(() => card.ShowsImage && square.ShowsImage), "the preview never showed the artwork");
        harness.All<ScrollViewer>().First().ScrollToEnd();

        Assert.Contains("skyrim-wide.png", card.ImagePath, StringComparison.Ordinal);
        Assert.Contains("skyrim-square.png", square.ImagePath, StringComparison.Ordinal);
        AssertRendered(harness.Render("editor-artwork-steam-dark", _area));
    }

    [AvaloniaFact]
    public async Task The_editor_shows_the_users_own_image_light()
    {
        using var fixtures = new ArtFixtures();
        var backend = BackendWithArt(fixtures, ThemePreference.Light);
        using var harness = await EditorViewHarness.StartAsync(backend, ThemePreference.Light, height: 1000);
        harness.Edit(SampleScenario.SatisfactoryId);
        var card = harness.Find<ArtTile>("ArtworkCardPreview");
        Assert.True(await UiWait.UntilAsync(() => card.ShowsImage), "the preview never showed the artwork");
        harness.All<ScrollViewer>().First().ScrollToEnd();

        AssertRendered(harness.Render("editor-artwork-own-image-light", _area));
    }

    [AvaloniaFact]
    public async Task The_editor_shows_a_game_with_no_artwork_and_a_refused_image()
    {
        using var fixtures = new ArtFixtures();
        var backend = BackendWithArt(fixtures, ThemePreference.Dark);
        using var harness = await EditorViewHarness.StartAsync(backend, height: 1000);
        var editor = harness.Edit(SampleScenario.CaretakerId);
        var text = new UiFormatter(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), backend.Clock, "windows", TimeZoneInfo.Utc);
        editor.ArtworkError = new ErrorCardViewModel(ErrorInfo.From(new ArtworkNotAnImageException("page.png")), text);
        editor.SteamAppIdText = "abc";
        harness.Settle();
        harness.All<ScrollViewer>().First().ScrollToEnd();

        Assert.False(harness.Find<ArtTile>("ArtworkCardPreview").ShowsImage);
        AssertRendered(harness.Render("editor-artwork-none-and-error-dark", _area));
    }
}
