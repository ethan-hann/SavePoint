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

    private async Task<LibraryGame> ScanSevenDaysAsync()
    {
        _steam.AddGame(_steam.SteamFolder, _appId, "7 Days to Die", "7 Days To Die");
        _steam.WriteLibraryList();
        return (await Games.ScanLibrariesAsync(_workspace.Token)).Value!.Games.Single();
    }

    private async Task<string> ModsOfAsync(LibraryGame game) => (await Games.ResolvePathAsync(game.Draft.Mods!.Path, _workspace.Token)).Value!;

    [Fact]
    public async Task The_draft_of_a_scanned_game_carries_the_templates_mods_folder_and_the_scan_says_whether_it_is_there()
    {
        var before = await ScanSevenDaysAsync();
        var mods = await ModsOfAsync(before);
        BackendWorkspace.WriteFile(Path.Combine(mods, "ModA", "ModInfo.xml"), "a mod");
        var after = (await Games.ScanLibrariesAsync(_workspace.Token)).Value!.Games.Single();

        Assert.Equal("7-days-to-die", before.TemplateId);
        Assert.EndsWith("Mods", before.Draft.Mods!.Path, StringComparison.Ordinal);
        Assert.Equal(mods, before.ModsFolder);
        Assert.False(before.ModsFolderExists);
        Assert.True(after.ModsFolderExists);
    }

    [Fact]
    public async Task A_mods_folder_the_scan_showed_as_there_is_tracked_with_the_game()
    {
        var first = await ScanSevenDaysAsync();
        var mods = await ModsOfAsync(first);
        BackendWorkspace.WriteFile(Path.Combine(mods, "ModA", "ModInfo.xml"), "a mod");
        var game = (await Games.ScanLibrariesAsync(_workspace.Token)).Value!.Games.Single();

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

    // A game the player added by hand: its own install folder, and the mods folder at the given place.
    private async Task<GameDefinition> AddByHandWithModsAsync(string modsFolder)
    {
        BackendWorkspace.WriteFile(Path.Combine(modsFolder, "ModA", "ModInfo.xml"), "a mod");
        return await _workspace.AddGameAsync(_workspace.NewGame("Mine", withSaves: false) with { Mods = new ModsLocation(modsFolder) });
    }

    [Fact]
    public async Task A_scanned_game_whose_mods_folder_is_a_mods_folder_of_an_added_game_is_marked_as_added()
    {
        var before = await ScanSevenDaysAsync();
        var existing = await AddByHandWithModsAsync(await ModsOfAsync(before));

        var game = (await Games.ScanLibrariesAsync(_workspace.Token)).Value!.Games.Single();

        Assert.True(game.AlreadyAdded);
        Assert.Equal(existing.Id, game.AddedGameId);
    }

    [Fact]
    public async Task A_scanned_game_is_not_marked_as_added_for_a_mods_folder_somewhere_else()
    {
        await ScanSevenDaysAsync();
        await AddByHandWithModsAsync(Path.Combine(_workspace.GamesRoot, "elsewhere", "mods"));

        var game = (await Games.ScanLibrariesAsync(_workspace.Token)).Value!.Games.Single();

        Assert.False(game.AlreadyAdded);
        Assert.Null(game.AddedGameId);
    }

    [Fact]
    public async Task Adding_a_scanned_game_whose_mods_folder_belongs_to_an_added_game_says_it_is_already_added()
    {
        var before = await ScanSevenDaysAsync();
        var existing = await AddByHandWithModsAsync(await ModsOfAsync(before));
        var game = (await Games.ScanLibrariesAsync(_workspace.Token)).Value!.Games.Single();

        var again = await Games.AddLibraryGameAsync(game);

        Assert.Equal(LibraryAddStatus.AlreadyAdded, again.Value!.Status);
        Assert.Equal(existing.Id, again.Value.GameId);
    }

    [Fact]
    public async Task A_mods_folder_the_game_has_not_made_yet_is_left_out_and_the_result_says_so()
    {
        var game = await ScanSevenDaysAsync();
        var mods = await ModsOfAsync(game);

        var added = await Games.AddLibraryGameAsync(game);

        Assert.True(added.Ok, added.Error?.Detail);
        Assert.Equal(LibraryAddStatus.Added, added.Value!.Status);
        Assert.Equal(mods, added.Value.ModsFolderLeftOut);
        Assert.Null((await Games.ListAsync(_workspace.Token)).Value!.Single().Mods);
        Assert.False(Directory.Exists(mods), "SavePoint must not make the game's folder");
    }

    [Fact]
    public async Task A_mods_folder_that_appeared_after_the_scan_is_left_out_because_the_player_was_not_shown_it()
    {
        var game = await ScanSevenDaysAsync();
        var mods = await ModsOfAsync(game);
        BackendWorkspace.WriteFile(Path.Combine(mods, "ModA", "ModInfo.xml"), "a mod");

        var added = await Games.AddLibraryGameAsync(game);

        Assert.True(added.Ok, added.Error?.Detail);
        Assert.Equal(mods, added.Value!.ModsFolderLeftOut);
        Assert.Null((await Games.ListAsync(_workspace.Token)).Value!.Single().Mods);
        Assert.IsType<ConsentMarkerState.Missing>(ConsentMarker.Read(mods));
    }
}
