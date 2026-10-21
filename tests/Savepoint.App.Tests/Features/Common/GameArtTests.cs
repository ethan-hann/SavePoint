using Savepoint.App.Features.Common.Art;
using Savepoint.Core.Api;

namespace Savepoint.App.Tests.Features.Common;

public class GameArtTests
{
    private static GameArt NewArt() => new("game-1", new ArtImages());

    private static GameArtwork Steam(string? wide = "/art/wide.jpg", string? square = "/art/square.jpg", int? appId = 489830, bool detected = false) =>
        new("game-1", ArtworkSource.Steam, wide, square, appId, detected);

    [Fact]
    public void A_new_artwork_has_nothing_to_show()
    {
        var art = NewArt();

        Assert.Equal("game-1", art.GameId);
        Assert.Equal(ArtworkSource.None, art.Source);
        Assert.Null(art.WidePath);
        Assert.Null(art.SquarePath);
        Assert.Null(art.SteamAppId);
        Assert.False(art.SteamAppIdDetected);
        Assert.False(art.HasArtwork);
        Assert.Equal(0, art.Revision);
    }

    [Fact]
    public void Showing_what_the_api_reported_takes_every_field_and_says_so_once()
    {
        var art = NewArt();
        var changed = new List<string?>();
        art.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        art.Show(Steam(detected: true));

        Assert.Equal(ArtworkSource.Steam, art.Source);
        Assert.Equal("/art/wide.jpg", art.WidePath);
        Assert.Equal("/art/square.jpg", art.SquarePath);
        Assert.Equal(489830, art.SteamAppId);
        Assert.True(art.SteamAppIdDetected);
        Assert.True(art.HasArtwork);
        Assert.Equal(1, art.Revision);
        Assert.Contains(nameof(GameArt.Revision), changed);
        Assert.Contains(nameof(GameArt.HasArtwork), changed);
    }

    [Fact]
    public void Showing_the_same_thing_again_changes_nothing()
    {
        var art = NewArt();
        art.Show(Steam());
        var changed = new List<string?>();
        art.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        art.Show(Steam());

        Assert.Equal(1, art.Revision);
        Assert.Empty(changed);
    }

    [Fact]
    public void The_same_paths_after_a_change_event_still_count_as_new_because_the_files_may_have_changed()
    {
        var art = NewArt();
        art.Show(Steam());

        art.Show(Steam(), contentMayHaveChanged: true);

        Assert.Equal(2, art.Revision);
    }

    [Fact]
    public void Showing_none_after_something_clears_it()
    {
        var art = NewArt();
        art.Show(Steam());

        art.Show(GameArtwork.None("game-1"));

        Assert.False(art.HasArtwork);
        Assert.Equal(ArtworkSource.None, art.Source);
        Assert.Null(art.WidePath);
        Assert.Null(art.SquarePath);
        Assert.Equal(2, art.Revision);
    }

    [Fact]
    public void A_change_in_the_app_id_alone_is_a_change()
    {
        var art = NewArt();
        art.Show(GameArtwork.None("game-1"));
        var before = art.Revision;

        art.Show(new GameArtwork("game-1", ArtworkSource.None, SteamAppId: 777, SteamAppIdDetected: true));

        Assert.Equal(777, art.SteamAppId);
        Assert.True(art.SteamAppIdDetected);
        Assert.Equal(before + 1, art.Revision);
    }

    [Theory]
    [InlineData("/w.jpg", "/s.jpg", ArtShape.Wide, "/w.jpg")]
    [InlineData("/w.jpg", "/s.jpg", ArtShape.Square, "/s.jpg")]
    [InlineData("/w.jpg", null, ArtShape.Square, "/w.jpg")]
    [InlineData("/w.jpg", null, ArtShape.Wide, "/w.jpg")]
    [InlineData(null, "/s.jpg", ArtShape.Wide, "/s.jpg")]
    [InlineData(null, "/s.jpg", ArtShape.Square, "/s.jpg")]
    [InlineData(null, null, ArtShape.Wide, null)]
    [InlineData(null, null, ArtShape.Square, null)]
    public void A_tile_draws_the_image_of_its_own_shape_else_the_other_one(string? wide, string? square, ArtShape shape, string? expected)
    {
        var art = NewArt();
        art.Show(Steam(wide, square));

        Assert.Equal(expected, art.PathFor(shape));
    }

    [Fact]
    public void The_users_own_image_is_the_same_file_for_every_shape()
    {
        var art = NewArt();
        art.Show(new GameArtwork("game-1", ArtworkSource.Custom, "/mine.png", "/mine.png"));

        Assert.Equal("/mine.png", art.PathFor(ArtShape.Wide));
        Assert.Equal("/mine.png", art.PathFor(ArtShape.Square));
        Assert.Equal(ArtworkSource.Custom, art.Source);
    }

    [Fact]
    public void The_first_tile_on_screen_tells_the_provider_and_later_ones_do_not()
    {
        var art = NewArt();
        var told = new List<GameArt>();
        art.OnScreen = told.Add;

        art.NotifyOnScreen();
        art.NotifyOnScreen();
        art.NotifyOnScreen();

        Assert.Equal([art], told);
    }

    [Fact]
    public void An_artwork_nobody_listens_to_can_be_put_on_screen_without_trouble()
    {
        var art = NewArt();

        art.NotifyOnScreen();

        Assert.False(art.HasArtwork);
    }
}
