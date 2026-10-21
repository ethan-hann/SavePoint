using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Savepoint.App.Features.Common.Art;
using Savepoint.App.Tests.Theme;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Common;

// The tile draws a game's image over its tint: uniform-to-fill inside its own rounded corners, decoded off the UI thread at the size
// it is shown, and never flickering back to the tint on a theme change or a rebuilt list.
public class ArtTileImageTests
{
    private const uint _green = 0xFF00AA00;
    private const uint _red = 0xFFCC0000;

    private static async Task<bool> UntilAsync(Func<bool> condition, int milliseconds = 8000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (!condition())
        {
            if (DateTime.UtcNow > until)
            {
                return false;
            }

            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        return true;
    }

    private static ArtTile NewTile(GameArt? art, double width, double height, CornerRadius? radius = null)
    {
        var tile = new ArtTile { GameName = "Satisfactory", Art = art, Width = width, Height = height, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        if (radius is { } corners)
        {
            tile.CornerRadius = corners;
        }

        return tile;
    }

    private static ThemeHost Host(ArtTile tile, ThemePreference theme = ThemePreference.Dark) =>
        new(new Grid { Children = { tile } }, theme, width: 260, height: 160);

    private static GameArt Custom(ArtImages images, string path) =>
        Shown(images, new GameArtwork("g", ArtworkSource.Custom, path, path));

    private static GameArt Shown(ArtImages images, GameArtwork artwork)
    {
        var art = new GameArt(artwork.GameId, images);
        art.Show(artwork);
        return art;
    }

    private static Color Pixel(Window window, int x, int y) => ThemeTestSupport.PixelAt(ThemeTestSupport.Render(window), x, y);

    [AvaloniaFact]
    public void A_tile_without_artwork_shows_only_the_tint()
    {
        var tile = NewTile(null, 40, 40);
        using var host = Host(tile);

        Assert.False(tile.ShowsImage);
        Assert.Null(tile.ImagePath);
        Assert.IsType<LinearGradientBrush>(tile.Background);
    }

    [AvaloniaFact]
    public void A_game_whose_artwork_has_no_images_keeps_the_tint()
    {
        var art = new GameArt("g", new ArtImages());
        var tile = NewTile(art, 40, 40);
        using var host = Host(tile);

        Assert.False(tile.ShowsImage);
    }

    [AvaloniaFact]
    public async Task A_tile_draws_the_decoded_image_over_the_tint()
    {
        using var fixtures = new ArtFixtures();
        var file = fixtures.Png("a.png", 300, 300, _green);
        var tile = NewTile(Custom(new ArtImages(), file), 40, 40);
        using var host = Host(tile);

        Assert.True(await UntilAsync(() => tile.ShowsImage), "the image was never drawn");

        Assert.Equal(file, tile.ImagePath);
        Assert.NotNull(tile.ImageBitmap);
        Assert.Equal(Color.FromUInt32(_green), Pixel(host.Window, 20, 20));
    }

    [AvaloniaFact]
    public async Task The_image_is_clipped_to_the_tiles_rounded_corners()
    {
        using var fixtures = new ArtFixtures();
        var file = fixtures.Png("a.png", 300, 300, _green);
        var tile = NewTile(Custom(new ArtImages(), file), 40, 40, new CornerRadius(8));
        using var host = Host(tile);
        Assert.True(await UntilAsync(() => tile.ShowsImage), "the image was never drawn");

        Assert.Equal(Color.FromUInt32(_green), Pixel(host.Window, 20, 20));
        Assert.NotEqual(Color.FromUInt32(_green), Pixel(host.Window, 1, 1));
        Assert.NotEqual(Color.FromUInt32(_green), Pixel(host.Window, 38, 38));
        Assert.Equal(Color.FromUInt32(_green), Pixel(host.Window, 20, 2));
    }

    [AvaloniaFact]
    public async Task A_card_with_square_bottom_corners_is_filled_to_them()
    {
        using var fixtures = new ArtFixtures();
        var file = fixtures.Png("wide.png", 460, 215, _green);
        var tile = NewTile(Custom(new ArtImages(), file), 202, 74, new CornerRadius(13, 13, 0, 0));
        using var host = Host(tile);
        Assert.True(await UntilAsync(() => tile.ShowsImage), "the image was never drawn");

        Assert.NotEqual(Color.FromUInt32(_green), Pixel(host.Window, 1, 1));
        Assert.Equal(Color.FromUInt32(_green), Pixel(host.Window, 1, 72));
        Assert.Equal(Color.FromUInt32(_green), Pixel(host.Window, 200, 72));
    }

    [AvaloniaFact]
    public async Task An_image_that_does_not_fit_the_tile_is_scaled_to_fill_it_and_cropped_to_the_middle()
    {
        using var fixtures = new ArtFixtures();
        // A wide image with a red middle: filling a square scales it to the tile's height and keeps only the middle, so no green is left.
        var file = fixtures.PngBands("wide.png", 400, 100, sides: _green, middle: _red);
        var tile = NewTile(Custom(new ArtImages(), file), 40, 40);
        using var host = Host(tile);
        Assert.True(await UntilAsync(() => tile.ShowsImage), "the image was never drawn");

        Assert.Equal(Color.FromUInt32(_red), Pixel(host.Window, 3, 20));
        Assert.Equal(Color.FromUInt32(_red), Pixel(host.Window, 36, 20));
        Assert.Equal(Color.FromUInt32(_red), Pixel(host.Window, 20, 36));
    }

    [AvaloniaTheory]
    [InlineData(202, 74, true)]
    [InlineData(150, 100, true)]
    [InlineData(149, 100, false)]
    [InlineData(120, 100, false)]
    [InlineData(40, 40, false)]
    [InlineData(40, 60, false)]
    public async Task A_tile_clearly_wider_than_tall_draws_the_wide_image_and_any_other_the_square_one(double width, double height, bool wide)
    {
        using var fixtures = new ArtFixtures();
        var wideFile = fixtures.Png("wide.png", 460, 215, _red);
        var squareFile = fixtures.Png("square.png", 300, 450, _green);
        var art = Shown(new ArtImages(), new GameArtwork("g", ArtworkSource.Steam, wideFile, squareFile));
        var tile = NewTile(art, width, height);
        using var host = Host(tile);

        Assert.True(await UntilAsync(() => tile.ShowsImage), "the image was never drawn");

        Assert.Equal(wide ? wideFile : squareFile, tile.ImagePath);
    }

    [AvaloniaTheory]
    [InlineData(true, 202, 74)]
    [InlineData(false, 40, 40)]
    public async Task A_game_with_only_one_of_the_images_draws_it_on_every_shape(bool onlyWide, double width, double height)
    {
        using var fixtures = new ArtFixtures();
        var file = fixtures.Png("only.png", 300, 300, _green);
        var artwork = onlyWide ? new GameArtwork("g", ArtworkSource.Steam, file, null) : new GameArtwork("g", ArtworkSource.Steam, null, file);
        var tile = NewTile(Shown(new ArtImages(), artwork), width, height);
        using var host = Host(tile);

        Assert.True(await UntilAsync(() => tile.ShowsImage), "the image was never drawn");

        Assert.Equal(file, tile.ImagePath);
    }

    [AvaloniaFact]
    public async Task The_image_is_decoded_at_the_size_the_tile_shows_it_not_at_its_own_size()
    {
        using var fixtures = new ArtFixtures();
        var file = fixtures.Png("big.png", 2000, 1000);
        var tile = NewTile(Custom(new ArtImages(), file), 40, 40);
        using var host = Host(tile);

        Assert.True(await UntilAsync(() => tile.ShowsImage), "the image was never drawn");

        Assert.Equal(80, tile.ImageBitmap!.PixelSize.Width);
    }

    [AvaloniaFact]
    public async Task Switching_between_light_and_dark_keeps_the_image_and_does_not_decode_again()
    {
        using var fixtures = new ArtFixtures();
        var images = new ArtImages();
        var tile = NewTile(Custom(images, fixtures.Png("a.png", 300, 300, _green)), 40, 40);
        using var host = Host(tile);
        Assert.True(await UntilAsync(() => tile.ShowsImage), "the image was never drawn");
        var bitmap = tile.ImageBitmap;
        var decodes = images.DecodeCount;
        var tint = ((LinearGradientBrush)tile.Background!).GradientStops[0].Color;

        ((App)Application.Current!).Theme.SetPreference(ThemePreference.Light);
        Dispatcher.UIThread.RunJobs();

        Assert.True(tile.ShowsImage);
        Assert.Same(bitmap, tile.ImageBitmap);
        Assert.Equal(decodes, images.DecodeCount);
        Assert.NotEqual(tint, ((LinearGradientBrush)tile.Background!).GradientStops[0].Color);
        Assert.Equal(Color.FromUInt32(_green), Pixel(host.Window, 20, 20));
    }

    [AvaloniaFact]
    public async Task A_tile_made_again_for_the_same_game_shows_the_image_at_once_from_the_cache()
    {
        using var fixtures = new ArtFixtures();
        var images = new ArtImages();
        var art = Custom(images, fixtures.Png("a.png", 300, 300, _green));
        var first = NewTile(art, 40, 40);
        using (var host = Host(first))
        {
            Assert.True(await UntilAsync(() => first.ShowsImage), "the image was never drawn");
        }

        var again = NewTile(art, 40, 40);
        using var second = Host(again);
        Dispatcher.UIThread.RunJobs();

        Assert.True(again.ShowsImage);
        Assert.Equal(1, images.DecodeCount);
    }

    [AvaloniaFact]
    public async Task A_new_image_replaces_the_old_one_only_once_it_is_ready()
    {
        using var fixtures = new ArtFixtures();
        var images = new ArtImages();
        var first = fixtures.Png("a.png", 300, 300, _green);
        var second = fixtures.Png("b.png", 300, 300, _red);
        var art = Custom(images, first);
        var tile = NewTile(art, 40, 40);
        using var host = Host(tile);
        Assert.True(await UntilAsync(() => tile.ShowsImage), "the image was never drawn");

        art.Show(new GameArtwork("g", ArtworkSource.Custom, second, second));

        Assert.True(tile.ShowsImage);
        Assert.Equal(first, tile.ImagePath);
        Assert.True(await UntilAsync(() => tile.ImagePath == second), "the new image was never drawn");
        Assert.Equal(Color.FromUInt32(_red), Pixel(host.Window, 20, 20));
    }

    [AvaloniaFact]
    public async Task A_file_replaced_under_the_same_name_is_read_again_when_the_artwork_says_it_may_have_changed()
    {
        using var fixtures = new ArtFixtures();
        var images = new ArtImages();
        var file = fixtures.Png("a.png", 300, 300, _green);
        var art = Custom(images, file);
        var tile = NewTile(art, 40, 40);
        using var host = Host(tile);
        Assert.True(await UntilAsync(() => tile.ShowsImage), "the image was never drawn");
        var before = tile.ImageBitmap;

        fixtures.Png("a.png", 300, 300, _red);
        art.Show(new GameArtwork("g", ArtworkSource.Custom, file, file), contentMayHaveChanged: true);

        Assert.True(await UntilAsync(() => !ReferenceEquals(tile.ImageBitmap, before)), "the new file was never read");
        Assert.Equal(Color.FromUInt32(_red), Pixel(host.Window, 20, 20));
    }

    [AvaloniaFact]
    public async Task Artwork_that_goes_away_leaves_the_tint()
    {
        using var fixtures = new ArtFixtures();
        var art = Custom(new ArtImages(), fixtures.Png("a.png", 300, 300, _green));
        var tile = NewTile(art, 40, 40);
        using var host = Host(tile);
        Assert.True(await UntilAsync(() => tile.ShowsImage), "the image was never drawn");

        art.Show(GameArtwork.None("g"));
        Dispatcher.UIThread.RunJobs();

        Assert.False(tile.ShowsImage);
        Assert.Null(tile.ImagePath);
        Assert.NotEqual(Color.FromUInt32(_green), Pixel(host.Window, 20, 20));
    }

    [AvaloniaFact]
    public async Task A_file_that_cannot_be_decoded_leaves_the_tint_without_a_fuss()
    {
        using var fixtures = new ArtFixtures();
        var images = new ArtImages();
        var tile = NewTile(Custom(images, fixtures.BrokenJpeg("broken.jpg", 460, 215)), 40, 40);
        using var host = Host(tile);

        Assert.True(await UntilAsync(() => images.DecodeCount == 1), "the file was never tried");
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Dispatcher.UIThread.RunJobs();

        Assert.False(tile.ShowsImage);
    }

    [AvaloniaFact]
    public void A_tile_in_a_window_tells_its_artwork_once_that_it_is_on_screen_and_one_outside_a_window_does_not()
    {
        var art = new GameArt("g", new ArtImages());
        var told = 0;
        art.OnScreen = _ => told++;
        var loose = NewTile(art, 40, 40);

        Assert.Equal(0, told);
        using var host = Host(NewTile(art, 40, 40));
        using var other = Host(NewTile(art, 24, 24));

        Assert.Equal(1, told);
        Assert.NotNull(loose);
    }

    [AvaloniaFact]
    public async Task A_tile_taken_out_of_the_window_stops_following_its_artwork()
    {
        using var fixtures = new ArtFixtures();
        var images = new ArtImages();
        var art = new GameArt("g", images);
        var tile = NewTile(art, 40, 40);
        var grid = new Grid { Children = { tile } };
        using var host = new ThemeHost(grid, ThemePreference.Dark, width: 260, height: 160);

        grid.Children.Remove(tile);
        Dispatcher.UIThread.RunJobs();
        art.Show(new GameArtwork("g", ArtworkSource.Custom, fixtures.Png("a.png", 300, 300), fixtures.Png("a.png", 300, 300)));
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, images.DecodeCount);
        Assert.False(tile.ShowsImage);
    }

    [AvaloniaFact]
    public async Task A_tile_with_an_image_is_still_decoration_for_assistive_technology()
    {
        using var fixtures = new ArtFixtures();
        var tile = NewTile(Custom(new ArtImages(), fixtures.Png("a.png", 300, 300)), 40, 40);
        using var host = Host(tile);
        Assert.True(await UntilAsync(() => tile.ShowsImage), "the image was never drawn");

        Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(tile));
    }

    [AvaloniaFact]
    public async Task A_tile_moved_to_another_artwork_follows_it()
    {
        using var fixtures = new ArtFixtures();
        var images = new ArtImages();
        var first = Custom(images, fixtures.Png("a.png", 300, 300, _green));
        var second = Custom(images, fixtures.Png("b.png", 300, 300, _red));
        var tile = NewTile(first, 40, 40);
        using var host = Host(tile);
        Assert.True(await UntilAsync(() => tile.ShowsImage), "the image was never drawn");

        tile.Art = second;

        Assert.True(await UntilAsync(() => tile.ImagePath == second.WidePath), "the other game's image was never drawn");
    }
}
