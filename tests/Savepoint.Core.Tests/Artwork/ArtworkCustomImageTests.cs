using Savepoint.Core.Api;
using Savepoint.Testing;

namespace Savepoint.Core.Tests.Artwork;

// The user's own image: copied in, always wins, and refused with a named error when it is not an image or is too big.
public class ArtworkCustomImageTests
{
    private const long _limit = 25L * 1024 * 1024;

    [Fact]
    public async Task A_chosen_image_is_copied_into_the_games_artwork_folder_and_used_for_every_shape()
    {
        using var rig = new ArtworkRig();
        var game = await rig.AddGameAsync();
        var source = TestImages.Write(rig.Workspace.Root, "mine.png", TestImages.Png(300, 200));

        var set = await rig.Artwork.SetCustomImageAsync(game.Id, source);

        Assert.True(set.Ok, set.Error?.Detail);
        Assert.Equal(ArtworkSource.Custom, set.Value.Source);
        Assert.Equal(set.Value.WidePath, set.Value.SquarePath);
        Assert.StartsWith(rig.ArtworkFolder(game.Id) + Path.DirectorySeparatorChar, set.Value.WidePath, StringComparison.Ordinal);
        Assert.NotEqual(source, set.Value.WidePath);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(set.Value.WidePath!));
    }

    [Fact]
    public async Task The_copy_does_not_depend_on_the_original()
    {
        using var rig = new ArtworkRig();
        var game = await rig.AddGameAsync();
        var source = TestImages.Write(rig.Workspace.Root, "mine.png", TestImages.Png(300, 200));
        var set = await rig.Artwork.SetCustomImageAsync(game.Id, source);

        File.Delete(source);

        var read = await rig.Artwork.GetAsync(game.Id, rig.Token);
        Assert.Equal(ArtworkSource.Custom, read.Value!.Source);
        Assert.True(File.Exists(read.Value.WidePath));
        Assert.Equal(set.Value!.WidePath, read.Value.WidePath);
    }

    [Theory]
    [InlineData("png")]
    [InlineData("jpeg")]
    [InlineData("webp")]
    public async Task Png_jpeg_and_webp_are_accepted_whatever_the_file_is_called(string kind)
    {
        using var rig = new ArtworkRig();
        var game = await rig.AddGameAsync();
        var content = kind switch
        {
            "png" => TestImages.Png(64, 64),
            "jpeg" => TestImages.Jpeg(64, 64),
            _ => TestImages.WebPLossless(64, 64),
        };
        var source = TestImages.Write(rig.Workspace.Root, "picture.dat", content);

        var set = await rig.Artwork.SetCustomImageAsync(game.Id, source);

        Assert.True(set.Ok, set.Error?.Detail);
        Assert.Equal(content, File.ReadAllBytes(set.Value.WidePath!));
    }

    [Fact]
    public async Task A_second_image_replaces_the_first_and_leaves_one_file()
    {
        using var rig = new ArtworkRig();
        var game = await rig.AddGameAsync();
        var png = TestImages.Write(rig.Workspace.Root, "a.png", TestImages.Png(64, 64));
        var jpeg = TestImages.Write(rig.Workspace.Root, "b.jpg", TestImages.Jpeg(80, 80));
        await rig.Artwork.SetCustomImageAsync(game.Id, png);

        var set = await rig.Artwork.SetCustomImageAsync(game.Id, jpeg);

        Assert.Equal(File.ReadAllBytes(jpeg), File.ReadAllBytes(set.Value!.WidePath!));
        Assert.Single(Directory.EnumerateFiles(rig.ArtworkFolder(game.Id)));
    }

    [Fact]
    public async Task A_file_that_is_not_an_image_is_refused_by_name_and_changes_nothing()
    {
        using var rig = new ArtworkRig();
        var game = await rig.AddGameAsync();
        var good = TestImages.Write(rig.Workspace.Root, "good.png", TestImages.Png(64, 64));
        await rig.Artwork.SetCustomImageAsync(game.Id, good);
        var before = ArtworkRig.Hash(Directory.EnumerateFiles(rig.ArtworkFolder(game.Id)).Single());
        var notes = TestImages.Write(rig.Workspace.Root, "notes.png", System.Text.Encoding.ASCII.GetBytes("not a picture"));
        var truncated = TestImages.Write(rig.Workspace.Root, "cut.png", TestImages.Png(64, 64)[..40]);
        var page = TestImages.Write(rig.Workspace.Root, "page.jpg", TestImages.Html());
        var gif = TestImages.Write(rig.Workspace.Root, "anim.png", TestImages.Gif());
        var changesBefore = rig.Changes.Count;

        foreach (var bad in new[] { notes, truncated, page, gif, Path.Combine(rig.Workspace.Root, "missing.png"), rig.Workspace.Root })
        {
            var result = await rig.Artwork.SetCustomImageAsync(game.Id, bad);

            Assert.Equal("error.artwork.notAnImage", result.Error?.MessageId);
            Assert.Equal(bad, result.Error!.Args["path"]);
        }

        Assert.Equal(before, ArtworkRig.Hash(Directory.EnumerateFiles(rig.ArtworkFolder(game.Id)).Single()));
        Assert.Equal(changesBefore, rig.Changes.Count);
    }

    [Fact]
    public async Task An_image_of_exactly_25_megabytes_is_accepted_and_one_byte_more_is_too_large()
    {
        using var rig = new ArtworkRig();
        var game = await rig.AddGameAsync();
        var atLimit = TestImages.Write(rig.Workspace.Root, "limit.jpg", TestImages.JpegOfSize(1000, 1000, _limit));
        var over = TestImages.Write(rig.Workspace.Root, "over.jpg", TestImages.JpegOfSize(1000, 1000, _limit + 1));

        var accepted = await rig.Artwork.SetCustomImageAsync(game.Id, atLimit);
        var refused = await rig.Artwork.SetCustomImageAsync(game.Id, over);

        Assert.True(accepted.Ok, accepted.Error?.Detail);
        Assert.Equal("error.artwork.tooLarge", refused.Error?.MessageId);
        Assert.Equal(over, refused.Error!.Args["path"]);
        Assert.Equal(25, refused.Error.Args["maxMegabytes"]);
        Assert.Equal(_limit, new FileInfo((await rig.Artwork.GetAsync(game.Id, rig.Token)).Value!.WidePath!).Length);
    }

    [Fact]
    public async Task A_huge_file_is_refused_as_too_large_without_being_judged_as_an_image()
    {
        using var rig = new ArtworkRig();
        var game = await rig.AddGameAsync();
        var huge = Path.Combine(rig.Workspace.Root, "huge.bin");
        using (var stream = File.Create(huge))
        {
            stream.SetLength(_limit * 2);
        }

        var result = await rig.Artwork.SetCustomImageAsync(game.Id, huge);

        Assert.Equal("error.artwork.tooLarge", result.Error?.MessageId);
        Assert.False(Directory.Exists(rig.ArtworkFolder(game.Id)));
    }

    [Fact]
    public async Task Removing_the_image_deletes_the_copy_and_the_game_goes_back_to_none()
    {
        using var rig = new ArtworkRig();
        var game = await rig.AddGameAsync();
        var source = TestImages.Write(rig.Workspace.Root, "mine.png", TestImages.Png(64, 64));
        var set = await rig.Artwork.SetCustomImageAsync(game.Id, source);

        var cleared = await rig.Artwork.ClearCustomImageAsync(game.Id);

        Assert.True(cleared.Ok, cleared.Error?.Detail);
        Assert.Equal(ArtworkSource.None, cleared.Value.Source);
        Assert.Null(cleared.Value.WidePath);
        Assert.Null(cleared.Value.SquarePath);
        Assert.False(File.Exists(set.Value!.WidePath));
    }

    [Fact]
    public async Task Removing_an_image_the_game_never_had_is_not_an_error_and_says_nothing_changed()
    {
        using var rig = new ArtworkRig();
        var game = await rig.AddGameAsync();

        var cleared = await rig.Artwork.ClearCustomImageAsync(game.Id);

        Assert.True(cleared.Ok, cleared.Error?.Detail);
        Assert.Equal(ArtworkSource.None, cleared.Value.Source);
        Assert.Empty(rig.Changes);
    }

    [Fact]
    public async Task The_image_is_still_there_after_a_restart()
    {
        using var rig = new ArtworkRig();
        var game = await rig.AddGameAsync();
        var source = TestImages.Write(rig.Workspace.Root, "mine.png", TestImages.Png(64, 64));
        var set = await rig.Artwork.SetCustomImageAsync(game.Id, source);

        rig.Restart();
        var read = await rig.Artwork.GetAsync(game.Id, rig.Token);

        Assert.Equal(ArtworkSource.Custom, read.Value!.Source);
        Assert.Equal(set.Value!.WidePath, read.Value.WidePath);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(read.Value.WidePath!));
    }

    [Fact]
    public async Task Choosing_and_removing_an_image_each_say_the_games_artwork_changed()
    {
        using var rig = new ArtworkRig();
        var game = await rig.AddGameAsync();
        var other = await rig.AddGameAsync("Beta");
        var source = TestImages.Write(rig.Workspace.Root, "mine.png", TestImages.Png(64, 64));

        await rig.Artwork.SetCustomImageAsync(game.Id, source);
        Assert.Equal([game.Id], rig.Changes);
        await rig.Artwork.ClearCustomImageAsync(game.Id);

        Assert.Equal([game.Id, game.Id], rig.Changes);
        Assert.DoesNotContain(other.Id, rig.Changes);
    }

    [Fact]
    public async Task The_users_image_beats_steams_and_removing_it_brings_steams_back()
    {
        using var rig = new ArtworkRig();
        var game = await rig.AddGameAsync(change: g => g with { SteamAppId = 489830 });
        rig.PutSteamCache(489830);
        var steam = await rig.LookedUpAsync(game.Id);
        var source = TestImages.Write(rig.Workspace.Root, "mine.png", TestImages.Png(64, 64));

        var custom = await rig.Artwork.SetCustomImageAsync(game.Id, source);
        var back = await rig.Artwork.ClearCustomImageAsync(game.Id);

        Assert.Equal(ArtworkSource.Steam, steam.Source);
        Assert.Equal(ArtworkSource.Custom, custom.Value!.Source);
        Assert.Equal(ArtworkSource.Steam, back.Value!.Source);
        Assert.Equal(steam.WidePath, back.Value.WidePath);
        Assert.Equal(steam.SquarePath, back.Value.SquarePath);
    }

    [Fact]
    public async Task Every_call_for_a_game_the_catalog_does_not_hold_says_so_and_creates_nothing()
    {
        using var rig = new ArtworkRig();
        var source = TestImages.Write(rig.Workspace.Root, "mine.png", TestImages.Png(64, 64));

        foreach (var id in new[] { "no-such-game", "../escape", @"..\escape", "" })
        {
            Assert.Equal("error.game.notFound", (await rig.Artwork.GetAsync(id, rig.Token)).Error?.MessageId);
            Assert.Equal("error.game.notFound", (await rig.Artwork.SetCustomImageAsync(id, source)).Error?.MessageId);
            Assert.Equal("error.game.notFound", (await rig.Artwork.ClearCustomImageAsync(id)).Error?.MessageId);
            Assert.Equal("error.game.notFound", (await rig.Artwork.LookAgainAsync(id)).Error?.MessageId);
        }

        Assert.False(Directory.Exists(rig.ArtworkRoot));
        Assert.False(Directory.Exists(Path.Combine(rig.Workspace.CatalogDirectory, "escape")));
    }

    [Fact]
    public async Task A_cancelled_read_fails_with_the_cancelled_id()
    {
        using var rig = new ArtworkRig();
        var game = await rig.AddGameAsync();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var read = await rig.Artwork.GetAsync(game.Id, cancelled.Token);

        Assert.Equal("error.operation.cancelled", read.Error?.MessageId);
    }
}
