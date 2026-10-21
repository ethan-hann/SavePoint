using Avalonia.Headless.XUnit;
using Savepoint.App.Features.Common.Art;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Features.Common;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.EndToEnd;

// The whole app over the fake backend: artwork the Api reports reaches every tile of the game, in both layouts, and nothing is read
// from the Api before a tile is on screen.
public class ArtworkEndToEndTests
{
    private static ArtTile TileOf(ComposedApp app, string gameId, Func<ArtTile, bool> where) =>
        app.All<ArtTile>().Single(tile => tile.Art?.GameId == gameId && where(tile));

    private static bool IsRailTile(ArtTile tile) => tile.Width == 24;

    private static bool IsHeaderTile(ArtTile tile) => tile.Width == 40;

    private static bool IsCard(ArtTile tile) => tile.Height == 74;

    private static string[] Ids(ComposedApp app) => [.. app.Shell.Games.Select(row => row.Id)];

    private static FakeBackend BackendWith(Action<FakeBackend> script, ShellLayout layout = ShellLayout.Sidebar)
    {
        var backend = new FakeBackend(ScenarioBuilder.Mockup().WithSettings(MockupScenario.Settings with { Layout = layout, Theme = ThemePreference.Dark }));
        script(backend);
        return backend;
    }

    [Fact]
    public async Task Nothing_is_read_from_the_artwork_api_while_no_tile_is_on_screen()
    {
        using var app = await ComposedApp.StartAsync();

        Assert.Empty(app.Backend.Calls.To<IArtworkApi>(nameof(IArtworkApi.GetAsync)));
    }

    [Fact]
    public async Task The_games_and_history_headers_follow_the_selected_games_artwork()
    {
        using var app = await ComposedApp.StartAsync();
        var ids = Ids(app);

        app.Shell.SelectGame(ids[0]);
        Assert.Same(app.Shell.Artwork.For(ids[0]), app.Games.Art);
        app.Shell.SelectGame(ids[1]);
        Assert.Same(app.Shell.Artwork.For(ids[1]), app.Games.Art);
        app.Shell.Navigate(NavDestination.History);
        Assert.Same(app.Shell.Artwork.For(ids[1]), app.History.Art);
        app.Shell.SelectGame(ids[0]);
        Assert.Same(app.Shell.Artwork.For(ids[0]), app.History.Art);
    }

    [AvaloniaFact]
    public async Task A_games_own_image_shows_in_its_rail_tile_and_the_other_games_keep_their_tint()
    {
        using var fixtures = new ArtFixtures();
        var mine = fixtures.Png("mine.png", 300, 300);
        string? gameId = null;
        var backend = BackendWith(b =>
        {
            gameId = b.Peek(() => b.State.Games[0].Id);
            b.Artwork.ScriptCustom(gameId, mine);
        });
        using var app = await ComposedApp.StartWithViewAsync(backend);

        var tile = TileOf(app, gameId!, IsRailTile);
        Assert.True(await UiWait.UntilAsync(() => tile.ShowsImage), "the rail tile never showed the image");

        Assert.Equal(mine, tile.ImagePath);
        Assert.All(Ids(app).Where(id => id != gameId), id => Assert.False(TileOf(app, id, IsRailTile).ShowsImage));
    }

    [AvaloniaFact]
    public async Task The_header_tile_shows_the_square_image_and_the_rail_tile_of_the_same_game_shares_it()
    {
        using var fixtures = new ArtFixtures();
        var wide = fixtures.Png("wide.png", 460, 215, 0xFFCC0000);
        var square = fixtures.Png("square.png", 300, 450, 0xFF00AA00);
        string? gameId = null;
        var backend = BackendWith(b =>
        {
            gameId = b.Peek(() => b.State.Games[0].Id);
            b.Artwork.ScriptSteamArrives(gameId, wide, square);
        });
        using var app = await ComposedApp.StartWithViewAsync(backend);

        var header = TileOf(app, gameId!, IsHeaderTile);
        var rail = TileOf(app, gameId!, IsRailTile);
        Assert.True(await UiWait.UntilAsync(() => header.ShowsImage && rail.ShowsImage), "the tiles never showed the image");

        Assert.Equal(square, header.ImagePath);
        Assert.Equal(square, rail.ImagePath);
        Assert.Same(header.Art, rail.Art);
    }

    [AvaloniaFact]
    public async Task In_the_library_layout_the_card_shows_the_wide_image()
    {
        using var fixtures = new ArtFixtures();
        var wide = fixtures.Png("wide.png", 460, 215, 0xFFCC0000);
        var square = fixtures.Png("square.png", 300, 450, 0xFF00AA00);
        string? gameId = null;
        var backend = BackendWith(
            b =>
            {
                gameId = b.Peek(() => b.State.Games[0].Id);
                b.Artwork.ScriptSteamArrives(gameId, wide, square);
            },
            ShellLayout.Library);
        using var app = await ComposedApp.StartWithViewAsync(backend, layout: ShellLayout.Library);

        var card = TileOf(app, gameId!, IsCard);
        Assert.True(await UiWait.UntilAsync(() => card.ShowsImage), "the card never showed the image");

        Assert.Equal(wide, card.ImagePath);
    }

    [AvaloniaFact]
    public async Task Artwork_that_arrives_after_the_window_is_up_appears_on_its_own()
    {
        using var fixtures = new ArtFixtures();
        var file = fixtures.Png("later.png", 300, 300);
        using var app = await ComposedApp.StartWithViewAsync();
        var id = Ids(app)[1];
        var tile = TileOf(app, id, IsRailTile);
        Assert.False(tile.ShowsImage);

        app.Backend.Artwork.ScriptSteamArrives(id, file, file);

        Assert.True(await UiWait.UntilAsync(() => tile.ShowsImage), "the tile never showed the new image");
        Assert.Equal(file, tile.ImagePath);
    }

    [AvaloniaFact]
    public async Task Rebuilding_the_list_does_not_send_a_tile_back_to_the_tint()
    {
        using var fixtures = new ArtFixtures();
        var file = fixtures.Png("mine.png", 300, 300);
        string? gameId = null;
        var backend = BackendWith(b =>
        {
            gameId = b.Peek(() => b.State.Games[0].Id);
            b.Artwork.ScriptCustom(gameId, file);
        });
        using var app = await ComposedApp.StartWithViewAsync(backend);
        Assert.True(await UiWait.UntilAsync(() => TileOf(app, gameId!, IsRailTile).ShowsImage), "the tile never showed the image");

        await app.Shell.RefreshLibraryAsync();
        app.Settle();

        Assert.True(TileOf(app, gameId!, IsRailTile).ShowsImage);
    }

    [AvaloniaFact]
    public async Task A_games_artwork_is_read_once_however_many_tiles_show_it()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        var id = Ids(app)[0];

        await app.Shell.RefreshLibraryAsync();
        app.Settle();

        Assert.Equal(1, app.Backend.Calls.To<IArtworkApi>(nameof(IArtworkApi.GetAsync)).Count(call => (string?)call.Args[0] == id));
    }
}
