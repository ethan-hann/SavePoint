using Savepoint.Core.Api;
using Savepoint.Core.Artwork;
using Savepoint.Testing;

namespace Savepoint.Core.Tests.Artwork;

// GOG games' artwork from the images GOG Galaxy already downloaded, found through the goggame-<id>.info files in the install folder.
public class ArtworkGogLookupTests
{
    private const string _base = "1423049311";
    private const string _addOn = "1256837418";

    [Fact]
    public async Task A_game_installed_by_gog_gets_both_images_from_galaxys_cache_copied_into_its_folder()
    {
        using var rig = new ArtworkRig();
        var game = await rig.AddGameAsync();
        ArtworkRig.PutGogInfo(game.Install.Path, _base, _base);
        rig.PutGogCache(_base);

        var art = await rig.LookedUpAsync(game.Id);

        Assert.Equal(ArtworkSource.Gog, art.Source);
        Assert.StartsWith(rig.ArtworkFolder(game.Id), art.WidePath, StringComparison.Ordinal);
        Assert.StartsWith(rig.ArtworkFolder(game.Id), art.SquarePath, StringComparison.Ordinal);
        Assert.NotEqual(art.WidePath, art.SquarePath);
        Assert.Equal(ImageFormat.WebP, ImageProbe.ReadFile(art.WidePath!)?.Format);
        Assert.Equal(TestImages.WebPLossy(1600, 650), File.ReadAllBytes(art.WidePath!));
        Assert.Equal(TestImages.WebPLossy(342, 482), File.ReadAllBytes(art.SquarePath!));
        Assert.Null(art.SteamAppId);
        Assert.Equal([game.Id], rig.Changes);
    }

    [Fact]
    public async Task The_base_game_is_looked_up_not_the_add_on_whose_file_sorts_first()
    {
        using var rig = new ArtworkRig();
        var game = await rig.AddGameAsync();
        ArtworkRig.PutGogInfo(game.Install.Path, _addOn, _base);
        ArtworkRig.PutGogInfo(game.Install.Path, _base, _base);
        rig.PutGogCache(_base, squareHeight: 600);
        rig.PutGogCache(_addOn, squareHeight: 700);

        var art = await rig.LookedUpAsync(game.Id);

        Assert.Equal(ArtworkSource.Gog, art.Source);
        Assert.Equal(TestImages.WebPLossy(342, 600), File.ReadAllBytes(art.SquarePath!));
    }

    [Fact]
    public async Task A_game_with_only_one_of_the_two_images_in_the_cache_has_one_path_and_a_null_for_the_other()
    {
        using var rig = new ArtworkRig();
        var game = await rig.AddGameAsync();
        ArtworkRig.PutGogInfo(game.Install.Path, _base);
        rig.PutGogCache(_base, wide: false);

        var art = await rig.LookedUpAsync(game.Id);

        Assert.Equal(ArtworkSource.Gog, art.Source);
        Assert.Null(art.WidePath);
        Assert.NotNull(art.SquarePath);
    }

    [Fact]
    public async Task The_images_are_found_again_after_a_restart_without_galaxy()
    {
        using var rig = new ArtworkRig();
        var game = await rig.AddGameAsync();
        ArtworkRig.PutGogInfo(game.Install.Path, _base);
        rig.PutGogCache(_base);
        var before = await rig.LookedUpAsync(game.Id);
        Directory.Delete(rig.GogRoot, recursive: true);

        rig.Restart();
        var after = await rig.Artwork.GetAsync(game.Id, rig.Token);

        Assert.Equal(ArtworkSource.Gog, after.Value!.Source);
        Assert.Equal(before.WidePath, after.Value.WidePath);
        Assert.Equal(before.SquarePath, after.Value.SquarePath);
    }

    [Fact]
    public async Task Nothing_in_galaxys_cache_is_no_artwork_and_the_cache_is_looked_at_again_after_a_restart()
    {
        using var rig = new ArtworkRig();
        var game = await rig.AddGameAsync();
        ArtworkRig.PutGogInfo(game.Install.Path, _base);

        var first = await rig.LookedUpAsync(game.Id);
        rig.PutGogCache(_base);
        rig.Restart();
        var second = await rig.LookedUpAsync(game.Id);

        Assert.Equal(ArtworkSource.None, first.Source);
        Assert.Equal(ArtworkSource.Gog, second.Source);
    }

    [Fact]
    public async Task A_computer_without_galaxy_gives_no_gog_artwork_and_no_error()
    {
        using var rig = new ArtworkRig();
        rig.Gog.Folder = null;
        var game = await rig.AddGameAsync();
        ArtworkRig.PutGogInfo(game.Install.Path, _base);

        var art = await rig.LookedUpAsync(game.Id);

        Assert.Equal(ArtworkSource.None, art.Source);
    }

    [Fact]
    public async Task A_game_with_no_info_file_gets_no_gog_artwork_whatever_galaxy_holds()
    {
        using var rig = new ArtworkRig();
        var game = await rig.AddGameAsync();
        rig.PutGogCache(_base);

        var art = await rig.LookedUpAsync(game.Id);

        Assert.Equal(ArtworkSource.None, art.Source);
    }

    [Fact]
    public async Task The_users_own_image_wins_and_galaxys_is_not_copied()
    {
        using var rig = new ArtworkRig();
        var game = await rig.AddGameAsync();
        ArtworkRig.PutGogInfo(game.Install.Path, _base);
        rig.PutGogCache(_base);
        var mine = TestImages.Write(rig.Workspace.Root, "mine.png", TestImages.Png(300, 200));
        await rig.Artwork.SetCustomImageAsync(game.Id, mine);

        var art = await rig.LookedUpAsync(game.Id);

        Assert.Equal(ArtworkSource.Custom, art.Source);
        Assert.Empty(Directory.EnumerateFiles(rig.ArtworkFolder(game.Id), "gog-*"));
    }

    [Fact]
    public async Task Steams_images_win_when_the_steam_cache_has_them()
    {
        using var rig = new ArtworkRig();
        var game = await rig.AddGameAsync(change: g => g with { SteamAppId = 489830 });
        ArtworkRig.PutGogInfo(game.Install.Path, _base);
        rig.PutGogCache(_base);
        rig.PutSteamCache(489830);

        var art = await rig.LookedUpAsync(game.Id);

        Assert.Equal(ArtworkSource.Steam, art.Source);
        Assert.Empty(Directory.EnumerateFiles(rig.ArtworkFolder(game.Id), "gog-*"));
    }

    [Fact]
    public async Task Galaxys_images_are_used_when_a_steam_id_is_known_but_steams_cache_has_nothing_and_no_request_is_made()
    {
        using var rig = new ArtworkRig(online: true);
        var game = await rig.AddGameAsync();
        File.WriteAllText(Path.Combine(game.Install.Path, "steam_appid.txt"), "489830\n");
        ArtworkRig.PutGogInfo(game.Install.Path, _base);
        rig.PutGogCache(_base);

        var art = await rig.LookedUpAsync(game.Id);

        Assert.Equal(ArtworkSource.Gog, art.Source);
        Assert.Equal(489830, art.SteamAppId);
        Assert.Empty(rig.Http!.Requests);
    }

    [Fact]
    public async Task Steams_servers_are_still_asked_when_neither_cache_has_the_game()
    {
        using var rig = new ArtworkRig(online: true);
        var game = await rig.AddGameAsync(change: g => g with { SteamAppId = 489830 });
        ArtworkRig.PutGogInfo(game.Install.Path, _base);

        await rig.LookedUpAsync(game.Id);

        Assert.NotEmpty(rig.Http!.Requests);
    }

    [Fact]
    public async Task A_game_pointed_at_another_gog_product_drops_the_old_images_and_takes_the_new_ones()
    {
        using var rig = new ArtworkRig();
        var game = await rig.AddGameAsync();
        ArtworkRig.PutGogInfo(game.Install.Path, _base);
        rig.PutGogCache(_base, squareHeight: 482);
        rig.PutGogCache("1207658995", squareHeight: 600);
        await rig.LookedUpAsync(game.Id);

        File.Delete(Path.Combine(game.Install.Path, $"goggame-{_base}.info"));
        ArtworkRig.PutGogInfo(game.Install.Path, "1207658995");
        var art = await rig.LookedUpAsync(game.Id);

        Assert.Equal(ArtworkSource.Gog, art.Source);
        Assert.Equal(TestImages.WebPLossy(342, 600), File.ReadAllBytes(art.SquarePath!));
    }

    [Fact]
    public async Task A_game_pointed_at_a_product_galaxy_has_nothing_for_shows_none_not_the_old_games_images()
    {
        using var rig = new ArtworkRig();
        var game = await rig.AddGameAsync();
        ArtworkRig.PutGogInfo(game.Install.Path, _base);
        rig.PutGogCache(_base);
        await rig.LookedUpAsync(game.Id);

        File.Delete(Path.Combine(game.Install.Path, $"goggame-{_base}.info"));
        ArtworkRig.PutGogInfo(game.Install.Path, "1228964594");
        var art = await rig.LookedUpAsync(game.Id);

        Assert.Equal(ArtworkSource.None, art.Source);
    }

    [Fact]
    public async Task An_install_folder_that_cannot_be_read_keeps_the_images_found_before()
    {
        using var rig = new ArtworkRig();
        var game = await rig.AddGameAsync();
        ArtworkRig.PutGogInfo(game.Install.Path, _base);
        rig.PutGogCache(_base);
        await rig.LookedUpAsync(game.Id);

        File.Delete(Path.Combine(game.Install.Path, $"goggame-{_base}.info"));
        var art = await rig.LookedUpAsync(game.Id);

        Assert.Equal(ArtworkSource.Gog, art.Source);
    }

    [Fact]
    public async Task Look_again_finds_images_galaxy_downloaded_since()
    {
        using var rig = new ArtworkRig();
        var game = await rig.AddGameAsync();
        ArtworkRig.PutGogInfo(game.Install.Path, _base);
        await rig.LookedUpAsync(game.Id);
        rig.PutGogCache(_base);

        var again = await rig.Artwork.LookAgainAsync(game.Id);

        Assert.Equal(ArtworkSource.Gog, again.Value!.Source);
    }
}
