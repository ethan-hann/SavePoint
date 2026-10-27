using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Paths;
using Savepoint.Core.Tests.Artwork;
using Savepoint.Core.Tests.Library;

namespace Savepoint.Core.Tests.Backend;

// Adding a scanned game whose template names a mods folder: SavePoint only tracks a folder that is there, and says when it left one out.
public sealed class ModsLibraryTests : IDisposable
{
    private const int _appId = 251570;

    private readonly SteamLibraryRig _steam = new();
    private readonly BackendWorkspace _workspace;

    public ModsLibraryTests()
    {
        _workspace = new BackendWorkspace(options => options with { SteamFolder = new FixedSteamFolder(_steam.SteamFolder), GogInstalls = new FakeGogInstalls(null) }, _steam.SteamFolder);
    }

    private IGamesApi Games => _workspace.Games;

    public void Dispose()
    {
        _workspace.Dispose();
        _steam.Dispose();
    }

    private async Task<(LibraryGame Game, string Mods)> ScanSevenDaysAsync()
    {
        _steam.AddGame(_steam.SteamFolder, _appId, "7 Days to Die", "7 Days To Die");
        _steam.WriteLibraryList();
        var game = (await Games.ScanLibrariesAsync(_workspace.Token)).Value!.Games.Single();
        var mods = (await Games.ResolvePathAsync(game.Draft.Mods!.Path, _workspace.Token)).Value!;
        return (game, mods);
    }

    [Fact]
    public async Task The_draft_of_a_scanned_game_carries_the_templates_mods_folder()
    {
        var (game, _) = await ScanSevenDaysAsync();

        Assert.Equal("7-days-to-die", game.TemplateId);
        Assert.EndsWith("Mods", game.Draft.Mods!.Path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_mods_folder_that_exists_is_tracked_with_the_game()
    {
        var (game, mods) = await ScanSevenDaysAsync();
        BackendWorkspace.WriteFile(Path.Combine(mods, "ModA", "ModInfo.xml"), "a mod");

        var added = await Games.AddLibraryGameAsync(game);

        Assert.True(added.Ok, added.Error?.Detail);
        Assert.Equal(LibraryAddStatus.Added, added.Value!.Status);
        Assert.Null(added.Value.ModsFolderLeftOut);
        var stored = (await Games.ListAsync(_workspace.Token)).Value!.Single();
        Assert.Equal(game.Draft.Mods, stored.Mods);
        Assert.Equal(stored.Id, Assert.IsType<ConsentMarkerState.Valid>(ConsentMarker.Read(mods)).Marker.GameId);
        var folders = (await Games.GetFolderStatusAsync(stored.Id, _workspace.Token)).Value!;
        Assert.Contains(RepoRole.Mods, folders.Select(folder => folder.Role));
    }

    [Fact]
    public async Task A_mods_folder_the_game_has_not_made_yet_is_left_out_and_the_result_says_so()
    {
        var (game, mods) = await ScanSevenDaysAsync();

        var added = await Games.AddLibraryGameAsync(game);

        Assert.True(added.Ok, added.Error?.Detail);
        Assert.Equal(LibraryAddStatus.Added, added.Value!.Status);
        Assert.Equal(mods, added.Value.ModsFolderLeftOut);
        Assert.Null((await Games.ListAsync(_workspace.Token)).Value!.Single().Mods);
        Assert.False(Directory.Exists(mods), "SavePoint must not make the game's folder");
    }
}
