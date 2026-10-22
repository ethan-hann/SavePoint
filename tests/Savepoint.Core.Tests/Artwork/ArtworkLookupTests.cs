using Savepoint.Core.Api;
using Savepoint.Core.Artwork;
using Savepoint.Core.Catalog;
using Savepoint.Testing;

namespace Savepoint.Core.Tests.Artwork;

// Steam artwork from the Steam client's own cache and the app id it is found by, with online lookup off or no handler at all.
public class ArtworkLookupTests
{
    private const int _app = 489830;

    [Fact]
    public async Task A_game_with_a_stored_app_id_gets_both_images_from_the_steam_cache_copied_into_its_folder()
    {
        using var rig = new ArtworkRig();
        var game = await rig.AddGameAsync(change: g => g with { SteamAppId = _app });
        rig.PutSteamCache(_app);

        var art = await rig.LookedUpAsync(game.Id);

        Assert.Equal(ArtworkSource.Steam, art.Source);
        Assert.Equal(_app, art.SteamAppId);
        Assert.False(art.SteamAppIdDetected);
        Assert.StartsWith(rig.ArtworkFolder(game.Id), art.WidePath, StringComparison.Ordinal);
        Assert.StartsWith(rig.ArtworkFolder(game.Id), art.SquarePath, StringComparison.Ordinal);
        Assert.NotEqual(art.WidePath, art.SquarePath);
        Assert.Equal(ImageFormat.Jpeg, ImageProbe.ReadFile(art.WidePath!)?.Format);
        Assert.Equal(TestImages.Jpeg(460, 215), File.ReadAllBytes(art.WidePath!));
        Assert.Equal(TestImages.Jpeg(600, 900), File.ReadAllBytes(art.SquarePath!));
        Assert.Equal([game.Id], rig.Changes);
    }

    [Fact]
    public async Task A_read_never_waits_for_the_lookup_and_the_first_one_finds_nothing_yet()
    {
        using var rig = new ArtworkRig();
        var game = await rig.AddGameAsync(change: g => g with { SteamAppId = _app });
        rig.PutSteamCache(_app);

        var first = await rig.Artwork.GetAsync(game.Id, rig.Token);
        await rig.WhenIdleAsync();
        var second = await rig.Artwork.GetAsync(game.Id, rig.Token);

        Assert.Equal(ArtworkSource.None, first.Value!.Source);
        Assert.Equal(ArtworkSource.Steam, second.Value!.Source);
    }

    [Fact]
    public async Task The_images_are_found_again_after_a_restart_without_looking_anything_up()
    {
        using var rig = new ArtworkRig();
        var game = await rig.AddGameAsync(change: g => g with { SteamAppId = _app });
        rig.PutSteamCache(_app);
        var before = await rig.LookedUpAsync(game.Id);
        Directory.Delete(rig.SteamRoot, recursive: true);

        rig.Restart();
        var after = await rig.Artwork.GetAsync(game.Id, rig.Token);

        Assert.Equal(ArtworkSource.Steam, after.Value!.Source);
        Assert.Equal(before.WidePath, after.Value.WidePath);
        Assert.Equal(before.SquarePath, after.Value.SquarePath);
    }

    [Fact]
    public async Task A_steam_game_with_only_one_of_the_two_images_has_one_path_and_a_null_for_the_other()
    {
        using var rig = new ArtworkRig();
        var game = await rig.AddGameAsync(change: g => g with { SteamAppId = _app });
        rig.PutSteamCache(_app, square: false);

        var art = await rig.LookedUpAsync(game.Id);

        Assert.Equal(ArtworkSource.Steam, art.Source);
        Assert.NotNull(art.WidePath);
        Assert.Null(art.SquarePath);
    }

    [Fact]
    public async Task An_id_found_from_the_launch_link_is_reported_as_detected_and_used()
    {
        using var rig = new ArtworkRig();
        var game = await rig.AddGameAsync(change: g => g with { Launch = new LaunchConfig(LaunchMethod.Uri, Uri: $"steam://run/{_app}") });
        rig.PutSteamCache(_app);

        var art = await rig.LookedUpAsync(game.Id);

        Assert.Equal(ArtworkSource.Steam, art.Source);
        Assert.Equal(_app, art.SteamAppId);
        Assert.True(art.SteamAppIdDetected);
    }

    [Fact]
    public async Task An_id_found_in_the_install_folder_is_used_when_the_game_has_none_stored()
    {
        using var rig = new ArtworkRig();
        var game = await rig.AddGameAsync();
        File.WriteAllText(Path.Combine(game.Install.Path, "steam_appid.txt"), $"{_app}\n");
        rig.PutSteamCache(_app);

        var art = await rig.LookedUpAsync(game.Id);

        Assert.Equal(ArtworkSource.Steam, art.Source);
        Assert.Equal(_app, art.SteamAppId);
        Assert.True(art.SteamAppIdDetected);
    }

    [Fact]
    public async Task A_stored_id_wins_over_anything_found()
    {
        using var rig = new ArtworkRig();
        var game = await rig.AddGameAsync(change: g => g with { SteamAppId = 111, Launch = new LaunchConfig(LaunchMethod.Uri, Uri: "steam://run/222") });
        rig.PutSteamCache(111, wide: true, square: false);
        rig.PutSteamCache(222, wide: false, square: true);

        var art = await rig.LookedUpAsync(game.Id);

        Assert.Equal(111, art.SteamAppId);
        Assert.False(art.SteamAppIdDetected);
        Assert.NotNull(art.WidePath);
        Assert.Null(art.SquarePath);
    }

    [Fact]
    public async Task The_app_id_is_reported_even_when_the_game_has_no_artwork_to_show()
    {
        using var rig = new ArtworkRig();
        var game = await rig.AddGameAsync(change: g => g with { Launch = new LaunchConfig(LaunchMethod.Uri, Uri: $"steam://run/{_app}") });

        var art = await rig.LookedUpAsync(game.Id);

        Assert.Equal(ArtworkSource.None, art.Source);
        Assert.Equal(_app, art.SteamAppId);
        Assert.True(art.SteamAppIdDetected);
    }

    [Fact]
    public async Task A_game_with_no_id_has_none_and_nothing_is_asked_of_the_network()
    {
        using var rig = new ArtworkRig(online: true);
        var game = await rig.AddGameAsync();
        rig.PutSteamCache(_app);

        var art = await rig.LookedUpAsync(game.Id);

        Assert.Equal(GameArtwork.None(game.Id), art);
        Assert.Empty(rig.Http!.Requests);
        Assert.Empty(rig.Changes);
    }

    [Fact]
    public async Task Detecting_and_looking_up_never_change_the_catalog()
    {
        using var rig = new ArtworkRig();
        var game = await rig.AddGameAsync(change: g => g with { Launch = new LaunchConfig(LaunchMethod.Uri, Uri: $"steam://run/{_app}") });
        rig.PutSteamCache(_app);
        var games = Path.Combine(rig.Workspace.CatalogDirectory, CatalogStore.GamesFileName);
        var before = ArtworkRig.Hash(games);

        await rig.LookedUpAsync(game.Id);
        await rig.Artwork.LookAgainAsync(game.Id);

        Assert.Equal(before, ArtworkRig.Hash(games));
        Assert.Null((await rig.Workspace.Backend.Services.Catalog.GetGameAsync(game.Id, rig.Token))!.SteamAppId);
    }

    [Fact]
    public async Task No_steam_folder_means_no_cache_and_no_error()
    {
        using var rig = new ArtworkRig();
        rig.Steam.Folder = null;
        var game = await rig.AddGameAsync(change: g => g with { SteamAppId = _app });

        var art = await rig.LookedUpAsync(game.Id);

        Assert.Equal(ArtworkSource.None, art.Source);
        Assert.Equal(_app, art.SteamAppId);
    }

    [Fact]
    public async Task A_cache_file_that_is_not_a_whole_image_is_not_kept()
    {
        using var rig = new ArtworkRig();
        var game = await rig.AddGameAsync(change: g => g with { SteamAppId = _app });
        TestImages.Write(Path.Combine(rig.SteamRoot, "appcache", "librarycache", _app.ToString()), "header.jpg", TestImages.Html());

        var art = await rig.LookedUpAsync(game.Id);

        Assert.Equal(ArtworkSource.None, art.Source);
        Assert.False(Directory.Exists(rig.ArtworkFolder(game.Id)) && Directory.EnumerateFiles(rig.ArtworkFolder(game.Id), "steam-*").Any());
    }

    [Fact]
    public async Task Changing_the_app_id_discards_the_old_images_at_once_and_finds_the_new_ones()
    {
        using var rig = new ArtworkRig();
        var game = await rig.AddGameAsync(change: g => g with { SteamAppId = 111 });
        rig.PutSteamCache(111);
        var old = await rig.LookedUpAsync(game.Id);
        rig.PutSteamCache(222, wide: true, square: false);
        await rig.UpdateGameAsync(game with { SteamAppId = 222 });

        var right = await rig.Artwork.GetAsync(game.Id, rig.Token);
        await rig.WhenIdleAsync();
        var later = await rig.Artwork.GetAsync(game.Id, rig.Token);

        Assert.Equal(ArtworkSource.Steam, old.Source);
        Assert.Equal(ArtworkSource.None, right.Value!.Source);
        Assert.Equal(222, right.Value.SteamAppId);
        Assert.Equal(ArtworkSource.Steam, later.Value!.Source);
        Assert.NotNull(later.Value.WidePath);
        Assert.Null(later.Value.SquarePath);
    }

    [Fact]
    public async Task A_game_with_its_own_image_starts_no_lookup()
    {
        using var rig = new ArtworkRig(online: true);
        var game = await rig.AddGameAsync(change: g => g with { SteamAppId = _app });
        rig.PutSteamCache(_app);
        var source = TestImages.Write(rig.Workspace.Root, "mine.png", TestImages.Png(64, 64));
        await rig.Artwork.SetCustomImageAsync(game.Id, source);

        var art = await rig.LookedUpAsync(game.Id);

        Assert.Equal(ArtworkSource.Custom, art.Source);
        Assert.Equal(_app, art.SteamAppId);
        Assert.Empty(rig.Http!.Requests);
        Assert.Empty(Directory.EnumerateFiles(rig.ArtworkFolder(game.Id), "steam-*"));
    }

    [Fact]
    public async Task Many_reads_at_once_start_one_lookup()
    {
        using var rig = new ArtworkRig();
        var game = await rig.AddGameAsync(change: g => g with { SteamAppId = _app });
        rig.PutSteamCache(_app);

        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => rig.Artwork.GetAsync(game.Id, rig.Token)));
        await rig.WhenIdleAsync();

        Assert.Equal([game.Id], rig.Changes);
    }

    [Fact]
    public async Task Looking_again_answers_when_the_lookup_is_done_and_says_what_it_found()
    {
        using var rig = new ArtworkRig();
        var game = await rig.AddGameAsync(change: g => g with { SteamAppId = _app });
        var nothing = await rig.Artwork.LookAgainAsync(game.Id);
        rig.PutSteamCache(_app);

        var found = await rig.Artwork.LookAgainAsync(game.Id);

        Assert.Equal(ArtworkSource.None, nothing.Value!.Source);
        Assert.Equal(ArtworkSource.Steam, found.Value!.Source);
        Assert.Equal([game.Id], rig.Changes);
    }

    [Fact]
    public async Task Looking_again_keeps_images_already_found_when_nothing_better_turns_up()
    {
        using var rig = new ArtworkRig();
        var game = await rig.AddGameAsync(change: g => g with { SteamAppId = _app });
        rig.PutSteamCache(_app);
        var before = await rig.LookedUpAsync(game.Id);
        Directory.Delete(rig.SteamRoot, recursive: true);

        var again = await rig.Artwork.LookAgainAsync(game.Id);

        Assert.Equal(ArtworkSource.Steam, again.Value!.Source);
        Assert.Equal(before.WidePath, again.Value.WidePath);
        Assert.True(File.Exists(again.Value.SquarePath));
    }

    [Fact]
    public async Task Looking_again_for_a_game_with_its_own_image_still_finds_steams_and_keeps_the_users_on_top()
    {
        using var rig = new ArtworkRig();
        var game = await rig.AddGameAsync(change: g => g with { SteamAppId = _app });
        var source = TestImages.Write(rig.Workspace.Root, "mine.png", TestImages.Png(64, 64));
        await rig.Artwork.SetCustomImageAsync(game.Id, source);
        rig.PutSteamCache(_app);

        var again = await rig.Artwork.LookAgainAsync(game.Id);
        await rig.Artwork.ClearCustomImageAsync(game.Id);
        var after = await rig.Artwork.GetAsync(game.Id, rig.Token);

        Assert.Equal(ArtworkSource.Custom, again.Value!.Source);
        Assert.Equal(ArtworkSource.Steam, after.Value!.Source);
    }
}
