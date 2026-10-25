using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Library;
using Savepoint.Core.Tests.Artwork;
using Savepoint.Core.Tests.Library;

namespace Savepoint.Core.Tests.Backend;

// The scan and the add over a whole backend: Steam and GOG libraries built from real manifests and records, the shipped Skyrim template.
public class GamesLibraryTests : IDisposable
{
    private readonly SteamLibraryRig _steam = new();
    private readonly FakeGogInstalls _gog = new(null);
    private readonly BackendWorkspace _workspace;
    private readonly FixedSteamFolder _steamFolder;

    public GamesLibraryTests()
    {
        _steamFolder = new FixedSteamFolder(_steam.SteamFolder);
        _workspace = new BackendWorkspace(options => options with { SteamFolder = _steamFolder, GogInstalls = _gog, Templates = ShippedTemplatesWithSkyrimSavesOnEveryOs.Build() }, _steam.SteamFolder);
    }

    private IGamesApi Games => _workspace.Games;

    private string SkyrimSaves => Path.Combine(_workspace.Platform.PathTokens["documents"], "My Games", "Skyrim Special Edition", "Saves");

    public void Dispose()
    {
        _workspace.Dispose();
        _steam.Dispose();
    }

    [Fact]
    public async Task Steam_games_come_before_gog_games_and_each_has_its_install_folder()
    {
        var skyrim = _steam.AddGame(_steam.SteamFolder, 489830, "The Elder Scrolls V: Skyrim Special Edition", "Skyrim Special Edition");
        var strike = _steam.AddGame(_steam.SteamFolder, 240, "Counter-Strike: Source", "Counter-Strike Source");
        _steam.AddGame(_steam.SteamFolder, 228980, "Steamworks Common Redistributables", "Steamworks Shared");
        _steam.WriteLibraryList();
        var deus = _workspace.GogFolder("Deus Ex GOTY");
        _gog.Records = [new GogInstallRecord("1207658995", "Deus Ex GOTY", deus, Path.Combine(deus, "DeusEx.exe"), "DeusEx.exe")];

        var scan = (await Games.ScanLibrariesAsync(_workspace.Token)).Value!;

        Assert.True(scan.SteamFound);
        Assert.True(scan.GogFound);
        Assert.Equal(["steam:240", "steam:489830", "gog:1207658995"], scan.Games.Select(g => g.Key));
        Assert.Equal(strike, scan.Games[0].InstallPath);
        Assert.Equal(skyrim, scan.Games[1].InstallPath);
        Assert.Equal(deus, scan.Games[2].InstallPath);
        Assert.Equal(LibrarySource.Gog, scan.Games[2].Source);
    }

    [Fact]
    public async Task A_store_that_is_not_installed_is_reported_as_not_found_and_is_not_an_error()
    {
        _steamFolder.Folder = null;

        var scanned = await Games.ScanLibrariesAsync(_workspace.Token);

        Assert.True(scanned.Ok, scanned.Error?.Detail);
        Assert.False(scanned.Value!.SteamFound);
        Assert.False(scanned.Value.GogFound);
        Assert.Empty(scanned.Value.Games);
    }

    [Fact]
    public async Task A_game_with_a_template_shows_the_saves_folder_the_template_names()
    {
        _steam.AddGame(_steam.SteamFolder, 489830, "The Elder Scrolls V: Skyrim Special Edition", "Skyrim Special Edition");
        _steam.WriteLibraryList();

        var before = (await Games.ScanLibrariesAsync(_workspace.Token)).Value!.Games.Single();
        Directory.CreateDirectory(SkyrimSaves);
        var after = (await Games.ScanLibrariesAsync(_workspace.Token)).Value!.Games.Single();

        Assert.Equal("skyrim-se", before.TemplateId);
        Assert.Equal("The Elder Scrolls V: Skyrim Special Edition", before.Name);
        Assert.Equal(SkyrimSaves, before.SavesFolder);
        Assert.False(before.SavesFolderExists);
        Assert.True(after.SavesFolderExists);
    }

    [Fact]
    public async Task A_template_game_is_matched_by_its_install_folder_when_the_app_id_is_unknown()
    {
        _steam.AddGame(_steam.SteamFolder, 1946180, "Skyrim Anniversary", "skyrim special edition");
        _steam.WriteLibraryList();

        var game = (await Games.ScanLibrariesAsync(_workspace.Token)).Value!.Games.Single();

        Assert.Equal("skyrim-se", game.TemplateId);
    }

    [Fact]
    public async Task The_draft_of_a_template_game_is_valid_and_carries_the_template_and_the_install_folder()
    {
        var folder = _steam.AddGame(_steam.SteamFolder, 489830, "The Elder Scrolls V: Skyrim Special Edition", "Skyrim Special Edition");
        File.WriteAllText(Path.Combine(folder, "SkyrimSELauncher.exe"), "x");
        _steam.WriteLibraryList();

        var draft = (await Games.ScanLibrariesAsync(_workspace.Token)).Value!.Games.Single().Draft;

        Assert.Equal(folder, draft.Install.Path);
        Assert.Equal(OperatingSystem.IsWindows() ? @"{documents}\My Games\Skyrim Special Edition\Saves" : "{documents}/My Games/Skyrim Special Edition/Saves", draft.External!.Path);
        // The template's program starts the game where it can run, and the Steam link everywhere else.
        Assert.Equal(OperatingSystem.IsWindows() ? "SkyrimSELauncher.exe" : null, draft.Launch.ExePath);
        Assert.Equal(OperatingSystem.IsWindows() ? LaunchMethod.Exe : LaunchMethod.Uri, draft.Launch.Method);
        Assert.Equal(489830, draft.SteamAppId);
        Assert.Empty(CatalogValidator.Validate(draft));
        Assert.StartsWith("the-elder-scrolls-v-skyrim-special-editi", draft.Id, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_game_with_no_template_is_offered_with_its_install_folder_only_and_a_draft_that_can_be_added()
    {
        // An app id and folder no shipped template names.
        var folder = _steam.AddGame(_steam.SteamFolder, 4999001, "Unlisted Test Game", "Unlisted Test Game");
        _steam.WriteLibraryList();

        var game = (await Games.ScanLibrariesAsync(_workspace.Token)).Value!.Games.Single();

        Assert.Null(game.TemplateId);
        Assert.Null(game.SavesFolder);
        Assert.False(game.SavesFolderExists);
        Assert.Equal("Unlisted Test Game", game.Name);
        Assert.Equal(folder, game.Draft.Install.Path);
        Assert.Null(game.Draft.External);
        Assert.Equal(4999001, game.Draft.SteamAppId);
        Assert.Empty(CatalogValidator.Validate(game.Draft));
    }

    [Fact]
    public async Task A_game_found_in_a_library_starts_out_leaving_out_logs_and_crash_dumps_with_or_without_a_template()
    {
        var skyrim = _steam.AddGame(_steam.SteamFolder, 489830, "The Elder Scrolls V: Skyrim Special Edition", "Skyrim Special Edition");
        File.WriteAllText(Path.Combine(skyrim, "SkyrimSELauncher.exe"), "x");
        _steam.AddGame(_steam.SteamFolder, 4999001, "Unlisted Test Game", "Unlisted Test Game");
        _steam.WriteLibraryList();

        var games = (await Games.ScanLibrariesAsync(_workspace.Token)).Value!.Games;

        var templated = games.Single(game => game.TemplateId == "skyrim-se").Draft;
        var plain = games.Single(game => game.TemplateId is null).Draft;
        Assert.Equal(["**/*.log", "SKSE/Plugins/*.log", "**/Crash Logs/**"], templated.IgnorePaths);
        Assert.Equal(["**/*.dmp", "**/*.mdmp"], templated.InstallIgnorePaths);
        Assert.Empty(plain.IgnorePaths);
        Assert.Equal(["**/*.log", "**/*.dmp", "**/*.mdmp"], plain.InstallIgnorePaths);
    }

    [Fact]
    public async Task A_game_whose_folder_is_already_a_games_folder_is_marked_as_added()
    {
        var folder = _steam.AddGame(_steam.SteamFolder, 240, "Counter-Strike: Source", "Counter-Strike Source");
        _steam.WriteLibraryList();
        var existing = _workspace.NewGame("Mine") with { Install = new InstallLocation(folder) };
        await _workspace.AddGameAsync(existing);

        var game = (await Games.ScanLibrariesAsync(_workspace.Token)).Value!.Games.Single();

        Assert.True(game.AlreadyAdded);
        Assert.Equal(existing.Id, game.AddedGameId);
    }

    [Fact]
    public async Task A_game_with_the_same_steam_app_id_is_marked_as_added_even_when_its_folder_was_moved()
    {
        _steam.AddGame(_steam.SteamFolder, 240, "Counter-Strike: Source", "Counter-Strike Source");
        _steam.WriteLibraryList();
        var existing = _workspace.NewGame("Mine") with { SteamAppId = 240 };
        await _workspace.AddGameAsync(existing);

        var game = (await Games.ScanLibrariesAsync(_workspace.Token)).Value!.Games.Single();

        Assert.Equal(existing.Id, game.AddedGameId);
    }

    [Fact]
    public async Task A_game_nobody_added_has_no_added_id()
    {
        _steam.AddGame(_steam.SteamFolder, 240, "Counter-Strike: Source", "Counter-Strike Source");
        _steam.WriteLibraryList();
        await _workspace.AddGameAsync();

        var game = (await Games.ScanLibrariesAsync(_workspace.Token)).Value!.Games.Single();

        Assert.False(game.AlreadyAdded);
    }

    [Fact]
    public async Task Scanning_writes_nothing_to_the_libraries_the_catalog_or_the_storage()
    {
        _steam.AddGame(_steam.SteamFolder, 489830, "The Elder Scrolls V: Skyrim Special Edition", "Skyrim Special Edition");
        _steam.WriteLibraryList();
        var before = BackendWorkspace.Snapshot(_steam.Root);

        await Games.ScanLibrariesAsync(_workspace.Token);

        Assert.Equal(before, BackendWorkspace.Snapshot(_steam.Root));
        Assert.Empty((await Games.ListAsync(_workspace.Token)).Value!);
        Assert.False(Directory.Exists(_workspace.DefaultStorageRoot) && Directory.EnumerateFileSystemEntries(_workspace.DefaultStorageRoot).Any());
    }

    [Fact]
    public async Task Adding_a_library_game_adds_it_with_both_folders_confirmed()
    {
        _steam.AddGame(_steam.SteamFolder, 489830, "The Elder Scrolls V: Skyrim Special Edition", "Skyrim Special Edition");
        _steam.WriteLibraryList();
        Directory.CreateDirectory(SkyrimSaves);
        var game = (await Games.ScanLibrariesAsync(_workspace.Token)).Value!.Games.Single();

        var added = await Games.AddLibraryGameAsync(game);

        Assert.True(added.Ok, added.Error?.Detail);
        Assert.Equal(LibraryAddStatus.Added, added.Value!.Status);
        Assert.Equal(game.Key, added.Value.Key);
        Assert.Equal(game.Draft.Id, added.Value.GameId);
        var listed = (await Games.ListAsync(_workspace.Token)).Value!;
        Assert.Equal([game.Draft.Id], listed.Select(g => g.Id));
        var folders = (await Games.GetFolderStatusAsync(game.Draft.Id, _workspace.Token)).Value!;
        Assert.Equal(2, folders.Count);
        Assert.All(folders, folder => Assert.True(folder.Consent.Confirmed));
        Assert.True((await Games.ScanLibrariesAsync(_workspace.Token)).Value!.Games.Single().AlreadyAdded);
    }

    [Fact]
    public async Task A_saves_folder_the_game_has_not_made_yet_is_left_out_and_the_result_says_so()
    {
        _steam.AddGame(_steam.SteamFolder, 489830, "The Elder Scrolls V: Skyrim Special Edition", "Skyrim Special Edition");
        _steam.WriteLibraryList();
        var game = (await Games.ScanLibrariesAsync(_workspace.Token)).Value!.Games.Single();

        var added = await Games.AddLibraryGameAsync(game);

        Assert.True(added.Ok, added.Error?.Detail);
        Assert.Equal(LibraryAddStatus.Added, added.Value!.Status);
        Assert.Equal(SkyrimSaves, added.Value.SavesFolderLeftOut);
        var stored = (await Games.ListAsync(_workspace.Token)).Value!.Single();
        Assert.Null(stored.External);
        Assert.False(Directory.Exists(SkyrimSaves));
    }

    [Fact]
    public async Task A_template_rule_that_would_hide_a_file_in_the_saves_folder_moves_to_the_games_own_folder_and_the_result_says_so()
    {
        _steam.AddGame(_steam.SteamFolder, 489830, "The Elder Scrolls V: Skyrim Special Edition", "Skyrim Special Edition");
        _steam.WriteLibraryList();
        Directory.CreateDirectory(SkyrimSaves);
        File.WriteAllText(Path.Combine(SkyrimSaves, "Player.log"), "kept");
        var game = (await Games.ScanLibrariesAsync(_workspace.Token)).Value!.Games.Single();

        var added = await Games.AddLibraryGameAsync(game);

        Assert.True(added.Ok, added.Error?.Detail);
        Assert.Equal(LibraryAddStatus.Added, added.Value!.Status);
        Assert.Equal(["**/*.log"], added.Value.RulesLeftOff?.Rules);
        var stored = (await Games.ListAsync(_workspace.Token)).Value!.Single();
        Assert.DoesNotContain("**/*.log", stored.IgnorePaths);
        Assert.Contains("**/*.log", stored.InstallIgnorePaths);
        Assert.Contains("SKSE/Plugins/*.log", stored.IgnorePaths);
    }

    [Fact]
    public async Task A_saves_folder_that_exists_is_kept_and_nothing_is_reported_left_out()
    {
        _steam.AddGame(_steam.SteamFolder, 489830, "The Elder Scrolls V: Skyrim Special Edition", "Skyrim Special Edition");
        _steam.WriteLibraryList();
        Directory.CreateDirectory(SkyrimSaves);
        var game = (await Games.ScanLibrariesAsync(_workspace.Token)).Value!.Games.Single();

        var added = await Games.AddLibraryGameAsync(game);

        Assert.Null(added.Value!.SavesFolderLeftOut);
        Assert.NotNull((await Games.ListAsync(_workspace.Token)).Value!.Single().External);
    }

    [Fact]
    public async Task A_game_without_a_template_is_added_with_its_install_folder_only()
    {
        _steam.AddGame(_steam.SteamFolder, 240, "Counter-Strike: Source", "Counter-Strike Source");
        _steam.WriteLibraryList();
        var game = (await Games.ScanLibrariesAsync(_workspace.Token)).Value!.Games.Single();

        var added = await Games.AddLibraryGameAsync(game);

        Assert.Equal(LibraryAddStatus.Added, added.Value!.Status);
        var folders = (await Games.GetFolderStatusAsync(game.Draft.Id, _workspace.Token)).Value!;
        Assert.Equal([RepoRole.Install], folders.Select(f => f.Role));
    }

    [Fact]
    public async Task Adding_a_game_that_is_already_in_the_library_changes_nothing_and_says_so()
    {
        _steam.AddGame(_steam.SteamFolder, 240, "Counter-Strike: Source", "Counter-Strike Source");
        _steam.WriteLibraryList();
        var game = (await Games.ScanLibrariesAsync(_workspace.Token)).Value!.Games.Single();
        await Games.AddLibraryGameAsync(game);

        var again = await Games.AddLibraryGameAsync(game);

        Assert.True(again.Ok, again.Error?.Detail);
        Assert.Equal(LibraryAddStatus.AlreadyAdded, again.Value!.Status);
        Assert.Equal(game.Draft.Id, again.Value.GameId);
        Assert.Single((await Games.ListAsync(_workspace.Token)).Value!);
    }

    [Fact]
    public async Task A_steam_library_folder_is_refused_with_the_reason_and_nothing_is_written()
    {
        var game = LibraryGameAt(_steam.SteamFolder);

        var added = await Games.AddLibraryGameAsync(game);

        Assert.True(added.Ok, added.Error?.Detail);
        Assert.Equal(LibraryAddStatus.Refused, added.Value!.Status);
        Assert.Equal("error.pathSafety.tier2Blocked", added.Value.Error?.MessageId);
        Assert.Null(added.Value.GameId);
        Assert.Empty((await Games.ListAsync(_workspace.Token)).Value!);
    }

    [Fact]
    public async Task A_refused_game_does_not_stop_the_next_one_from_being_added()
    {
        _steam.AddGame(_steam.SteamFolder, 240, "Counter-Strike: Source", "Counter-Strike Source");
        _steam.WriteLibraryList();
        var good = (await Games.ScanLibrariesAsync(_workspace.Token)).Value!.Games.Single();
        var bad = LibraryGameAt(_steam.SteamFolder);

        var results = new List<LibraryAddResult>();
        foreach (var game in new[] { bad, good })
        {
            results.Add((await Games.AddLibraryGameAsync(game)).Value!);
        }

        Assert.Equal([LibraryAddStatus.Refused, LibraryAddStatus.Added], results.Select(r => r.Status));
        Assert.Equal([good.Draft.Id], (await Games.ListAsync(_workspace.Token)).Value!.Select(g => g.Id));
    }

    [Fact]
    public async Task A_game_whose_folders_hold_history_from_a_removed_game_is_reported_and_not_added()
    {
        var removed = await _workspace.AddGameAsync();
        await _workspace.CreateProfileAsync(removed, "Vanilla");
        File.Delete(Path.Combine(_workspace.CatalogDirectory, CatalogStore.GamesFileName));
        File.Delete(Path.Combine(_workspace.CatalogDirectory, CatalogStore.ProfilesFileName));
        _workspace.Restart();
        var game = LibraryGameAt(removed.Install.Path) with { Name = "Alpha Again" };

        var added = await Games.AddLibraryGameAsync(game);

        Assert.True(added.Ok, added.Error?.Detail);
        Assert.Equal(LibraryAddStatus.HasOlderHistory, added.Value!.Status);
        Assert.Empty((await Games.ListAsync(_workspace.Token)).Value!);
    }

    private LibraryGame LibraryGameAt(string installPath)
    {
        var draft = new GameDefinition(Id: "library-game-test", Name: "Library Game", Install: new InstallLocation(installPath), Launch: new LaunchConfig(LaunchMethod.Uri, Uri: "steam://rungameid/1"));
        return new LibraryGame("steam:1", LibrarySource.Steam, "1", "Library Game", installPath, null, null, false, draft, null);
    }
}
