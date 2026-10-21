using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Savepoint.App.Features.Common.Art;

namespace Savepoint.App.Tests.Features.Common;

// Decoding is the one place artwork touches the disk from the app: off the UI thread, at the size a tile shows, never held open.
public class ArtImagesTests
{
    private static ArtKey Key(string path, int width = 40, int height = 40, int scale = 100, int revision = 0) =>
        new(path, revision, width, height, scale);

    [AvaloniaFact]
    public async Task A_wide_image_in_a_square_tile_is_decoded_to_cover_the_tile_and_no_larger()
    {
        using var fixtures = new ArtFixtures();
        var file = fixtures.Png("wide.png", 2000, 1000);

        var bitmap = await new ArtImages().LoadAsync(Key(file), TestContext.Current.CancellationToken);

        // The shorter side must reach 40, so the width is 80.
        Assert.NotNull(bitmap);
        Assert.Equal(80, bitmap.PixelSize.Width);
        Assert.Equal(40, bitmap.PixelSize.Height);
    }

    [AvaloniaFact]
    public async Task A_tall_image_in_a_square_tile_is_decoded_to_the_tile_width()
    {
        using var fixtures = new ArtFixtures();
        var file = fixtures.Png("tall.png", 600, 900);

        var bitmap = await new ArtImages().LoadAsync(Key(file), TestContext.Current.CancellationToken);

        Assert.NotNull(bitmap);
        Assert.Equal(40, bitmap.PixelSize.Width);
        Assert.Equal(60, bitmap.PixelSize.Height);
    }

    [AvaloniaFact]
    public async Task The_render_scaling_multiplies_the_decoded_size()
    {
        using var fixtures = new ArtFixtures();
        var file = fixtures.Png("tall.png", 600, 900);

        var bitmap = await new ArtImages().LoadAsync(Key(file, scale: 150), TestContext.Current.CancellationToken);

        Assert.NotNull(bitmap);
        Assert.Equal(60, bitmap.PixelSize.Width);
    }

    [AvaloniaFact]
    public async Task A_card_is_decoded_at_its_width()
    {
        using var fixtures = new ArtFixtures();
        var file = fixtures.Jpeg("header.jpg", 460, 215);

        var bitmap = await new ArtImages().LoadAsync(Key(file, width: 202, height: 74), TestContext.Current.CancellationToken);

        Assert.NotNull(bitmap);
        Assert.Equal(202, bitmap.PixelSize.Width);
    }

    [AvaloniaFact]
    public async Task An_image_is_never_enlarged_past_its_own_size()
    {
        using var fixtures = new ArtFixtures();
        var file = fixtures.Png("small.png", 100, 50);

        var bitmap = await new ArtImages().LoadAsync(Key(file, width: 400, height: 400), TestContext.Current.CancellationToken);

        Assert.NotNull(bitmap);
        Assert.Equal(100, bitmap.PixelSize.Width);
    }

    [AvaloniaFact]
    public async Task The_second_request_for_the_same_thing_is_served_from_the_cache_without_decoding_again()
    {
        using var fixtures = new ArtFixtures();
        var images = new ArtImages();
        var key = Key(fixtures.Png("a.png", 200, 200));

        var first = await images.LoadAsync(key, TestContext.Current.CancellationToken);
        var second = await images.LoadAsync(key, TestContext.Current.CancellationToken);

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Same(first, images.TryGet(key));
        Assert.Equal(1, images.DecodeCount);
    }

    [AvaloniaFact]
    public async Task Requests_made_at_the_same_time_share_one_decode()
    {
        using var fixtures = new ArtFixtures();
        var images = new ArtImages();
        var key = Key(fixtures.Png("a.png", 400, 400));

        var all = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => images.LoadAsync(key, TestContext.Current.CancellationToken)));

        Assert.All(all, bitmap => Assert.Same(all[0], bitmap));
        Assert.Equal(1, images.DecodeCount);
    }

    [AvaloniaFact]
    public async Task A_different_size_or_revision_is_decoded_afresh()
    {
        using var fixtures = new ArtFixtures();
        var images = new ArtImages();
        var file = fixtures.Png("a.png", 400, 400);

        await images.LoadAsync(Key(file), TestContext.Current.CancellationToken);
        await images.LoadAsync(Key(file, width: 80, height: 80), TestContext.Current.CancellationToken);
        await images.LoadAsync(Key(file, revision: 1), TestContext.Current.CancellationToken);

        Assert.Equal(3, images.DecodeCount);
    }

    [AvaloniaFact]
    public async Task Decoding_runs_off_the_ui_thread()
    {
        using var fixtures = new ArtFixtures();
        var images = new ArtImages();
        bool? onUiThread = null;
        images.Decoding = _ => onUiThread = Dispatcher.UIThread.CheckAccess();

        await images.LoadAsync(Key(fixtures.Png("a.png", 100, 100)), TestContext.Current.CancellationToken);

        Assert.False(onUiThread);
    }

    [AvaloniaFact]
    public async Task A_file_that_cannot_be_decoded_gives_nothing_and_is_not_tried_again()
    {
        using var fixtures = new ArtFixtures();
        var images = new ArtImages();
        var key = Key(fixtures.BrokenJpeg("broken.jpg", 460, 215));

        var first = await images.LoadAsync(key, TestContext.Current.CancellationToken);
        var second = await images.LoadAsync(key, TestContext.Current.CancellationToken);

        Assert.Null(first);
        Assert.Null(second);
        Assert.Equal(1, images.DecodeCount);
    }

    [AvaloniaFact]
    public async Task A_file_that_is_not_an_image_or_is_missing_gives_nothing()
    {
        using var fixtures = new ArtFixtures();
        var images = new ArtImages();

        Assert.Null(await images.LoadAsync(Key(fixtures.NotAnImage("page.jpg")), TestContext.Current.CancellationToken));
        Assert.Null(await images.LoadAsync(Key(Path.Combine(fixtures.Folder, "missing.png")), TestContext.Current.CancellationToken));
    }

    [AvaloniaFact]
    public async Task The_file_is_not_held_open_once_it_is_decoded()
    {
        using var fixtures = new ArtFixtures();
        var file = fixtures.Png("a.png", 100, 100);

        var bitmap = await new ArtImages().LoadAsync(Key(file), TestContext.Current.CancellationToken);
        File.Delete(file);

        Assert.NotNull(bitmap);
        Assert.False(File.Exists(file));
    }

    [AvaloniaFact]
    public async Task A_file_another_program_has_open_for_reading_can_still_be_decoded()
    {
        using var fixtures = new ArtFixtures();
        var file = fixtures.Png("a.png", 100, 100);
        using var other = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);

        var bitmap = await new ArtImages().LoadAsync(Key(file), TestContext.Current.CancellationToken);

        Assert.NotNull(bitmap);
    }
}
