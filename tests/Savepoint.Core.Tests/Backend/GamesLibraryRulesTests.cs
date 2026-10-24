using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Library;
using Savepoint.Core.Tests.Artwork;
using Savepoint.Core.Tests.Library;

namespace Savepoint.Core.Tests.Backend;

// What the scan reports and what the add does must agree, and the add must do only what the player was shown.
public class GamesLibraryRulesTests : IDisposable
{
    private readonly SteamLibraryRig _steam = new();
    private readonly FakeGogInstalls _gog = new(null);
    private readonly FixedSteamFolder _steamFolder;
    private BackendWorkspace _workspace = null!;

    public GamesLibraryRulesTests()
    {
        _steamFolder = new FixedSteamFolder(_steam.SteamFolder);
        Use(null);
    }

    private IGamesApi Games => _workspace.Games;

    private string SkyrimSaves => Path.Combine(_workspace.Platform.PathTokens["documents"], "My Games", "Skyrim Special Edition", "Saves");

    public void Dispose()
    {
        _workspace.Dispose();
        _steam.Dispose();
    }

    // Starts the backend with these templates instead of the shipped ones.
    private void Use(IReadOnlyList<GameTemplate>? templates)
    {
        _workspace?.Dispose();
        _workspace = new BackendWorkspace(options => options with { SteamFolder = _steamFolder, GogInstalls = _gog, Templates = templates ?? ShippedTemplatesWithSkyrimSavesOnEveryOs.Build() }, _steam.SteamFolder);
    }

    private static GameTemplate Template(string id, int? steamAppId, TemplateLibraryHints? hints = null, string exe = "game.exe") =>
        new(id, "Template " + id, new GameDefinition(Id: id, Name: "Template " + id, SteamAppId: steamAppId, Launch: new LaunchConfig(LaunchMethod.Exe, exe)), hints);

    private async Task<LibraryScan> ScanAsync() => (await Games.ScanLibrariesAsync(_workspace.Token)).Value!;

    [Fact]
    public async Task A_saves_folder_made_after_the_scan_is_not_added_because_the_player_was_not_shown_it()
    {
        _steam.AddGame(_steam.SteamFolder, 489830, "The Elder Scrolls V: Skyrim Special Edition", "Skyrim Special Edition");
        _steam.WriteLibraryList();
        var game = (await ScanAsync()).Games.Single();
        Directory.CreateDirectory(SkyrimSaves);

        var added = await Games.AddLibraryGameAsync(game);

        Assert.Equal(LibraryAddStatus.Added, added.Value!.Status);
        Assert.Equal(SkyrimSaves, added.Value.SavesFolderLeftOut);
        Assert.Null((await Games.ListAsync(_workspace.Token)).Value!.Single().External);
    }

    [Fact]
    public async Task Only_the_folders_the_scan_showed_are_added_whatever_the_draft_holds()
    {
        var folder = _steam.AddGame(_steam.SteamFolder, 489830, "The Elder Scrolls V: Skyrim Special Edition", "Skyrim Special Edition");
        _steam.WriteLibraryList();
        Directory.CreateDirectory(SkyrimSaves);
        var elsewhere = Path.Combine(_workspace.GamesRoot, "elsewhere");
        Directory.CreateDirectory(elsewhere);
        var shown = (await ScanAsync()).Games.Single();
        var tampered = shown with { Draft = shown.Draft with { Install = new InstallLocation(elsewhere), External = new ExternalLocation(elsewhere + "-saves") } };
        Directory.CreateDirectory(elsewhere + "-saves");

        var added = await Games.AddLibraryGameAsync(tampered);

        Assert.Equal(LibraryAddStatus.Added, added.Value!.Status);
        var stored = (await Games.ListAsync(_workspace.Token)).Value!.Single();
        Assert.Equal(folder, stored.Install.Path);
        Assert.Null(stored.External);
    }

    [Fact]
    public async Task A_gog_copy_of_a_templated_game_is_not_called_added_because_the_steam_copy_is()
    {
        Use([Template("t", 777, new TemplateLibraryHints(GogIds: ["55"]), "DeusEx.exe")]);
        await _workspace.AddGameAsync(_workspace.NewGame("Steamcopy") with { SteamAppId = 777 });
        var deus = _workspace.GogFolder("Deus Ex GOTY");
        _gog.Records = [new GogInstallRecord("55", "Deus Ex GOTY", deus, Path.Combine(deus, "DeusEx.exe"), "DeusEx.exe")];

        var game = (await ScanAsync()).Games.Single();
        var added = await Games.AddLibraryGameAsync(game);

        Assert.Equal("t", game.TemplateId);
        Assert.False(game.AlreadyAdded);
        Assert.Equal(LibraryAddStatus.Added, added.Value!.Status);
    }

    [Fact]
    public async Task A_steam_edition_matched_by_a_hint_keeps_its_own_app_id_and_is_judged_by_it()
    {
        Use([Template("t", 777, new TemplateLibraryHints(SteamAppIds: [778]))]);
        var folder = _steam.AddGame(_steam.SteamFolder, 778, "Template Anniversary", "Anniversary");
        File.WriteAllText(Path.Combine(folder, "game.exe"), "x");
        _steam.WriteLibraryList();
        await _workspace.AddGameAsync(_workspace.NewGame("Main") with { SteamAppId = 777 });

        var game = (await ScanAsync()).Games.Single();

        Assert.Equal("t", game.TemplateId);
        Assert.Equal(778, game.Draft.SteamAppId);
        Assert.False(game.AlreadyAdded);
    }

    [Fact]
    public async Task A_game_whose_saves_folder_another_game_already_tracks_is_called_added()
    {
        _steam.AddGame(_steam.SteamFolder, 489830, "The Elder Scrolls V: Skyrim Special Edition", "Skyrim Special Edition");
        _steam.WriteLibraryList();
        Directory.CreateDirectory(SkyrimSaves);
        var other = _workspace.NewGame("Other") with { External = new ExternalLocation(SkyrimSaves) };
        await _workspace.AddGameAsync(other);

        var game = (await ScanAsync()).Games.Single();

        Assert.Equal(other.Id, game.AddedGameId);
    }

    [Fact]
    public async Task A_folder_that_holds_another_installed_games_folder_is_not_offered_as_a_game()
    {
        var parent = Path.Combine(_workspace.GamesRoot, "gog");
        Directory.CreateDirectory(Path.Combine(parent, "Deus Ex GOTY"));
        File.WriteAllText(Path.Combine(parent, "Deus Ex GOTY", "DeusEx.exe"), "x");
        _gog.Records =
        [
            new GogInstallRecord("1", "All My Games", parent),
            new GogInstallRecord("2", "Deus Ex GOTY", Path.Combine(parent, "Deus Ex GOTY"), Path.Combine(parent, "Deus Ex GOTY", "DeusEx.exe"), "DeusEx.exe"),
        ];

        var scan = await ScanAsync();

        Assert.Equal(["gog:2"], scan.Games.Select(g => g.Key));
    }

    [Fact]
    public async Task Apps_that_share_one_folder_are_listed_once_under_the_lowest_app_id()
    {
        _steam.AddGame(_steam.SteamFolder, 1946160, "Fallout 4: Creation Kit", "Fallout 4");
        _steam.AddGame(_steam.SteamFolder, 377160, "Fallout 4", "Fallout 4");
        _steam.WriteLibraryList();

        var scan = await ScanAsync();

        Assert.Equal(["steam:377160"], scan.Games.Select(g => g.Key));
    }

    [Fact]
    public async Task Apps_that_share_one_folder_are_listed_under_the_one_a_template_describes()
    {
        Use([Template("kit", 1946160)]);
        _steam.AddGame(_steam.SteamFolder, 1946160, "Fallout 4: Creation Kit", "Fallout 4");
        _steam.AddGame(_steam.SteamFolder, 377160, "Fallout 4", "Fallout 4");
        _steam.WriteLibraryList();

        var scan = await ScanAsync();

        Assert.Equal(["steam:1946160"], scan.Games.Select(g => g.Key));
        Assert.Equal("kit", scan.Games[0].TemplateId);
    }

    [Fact]
    public async Task A_game_that_starts_through_a_store_link_gets_the_program_to_watch_so_its_session_can_end()
    {
        var folder = _steam.AddGame(_steam.SteamFolder, 4242, "Hollow Quest", "Hollow Quest");
        File.WriteAllBytes(Path.Combine(folder, "HollowQuest.exe"), new byte[6 << 20]);
        File.WriteAllBytes(Path.Combine(folder, "UnityCrashHandler64.exe"), new byte[1 << 20]);
        _steam.WriteLibraryList();
        var game = (await ScanAsync()).Games.Single();

        var added = await Games.AddLibraryGameAsync(game);

        Assert.Equal(LibraryAddStatus.Added, added.Value!.Status);
        var stored = (await Games.ListAsync(_workspace.Token)).Value!.Single();
        Assert.Equal(LaunchMethod.Uri, stored.Launch.Method);
        Assert.Equal("HollowQuest.exe", stored.Launch.ProcessNameForExitDetection);
    }

    [Fact]
    public async Task A_game_with_no_program_in_its_folder_is_still_added_without_a_process_name()
    {
        _steam.AddGame(_steam.SteamFolder, 4243, "Bare Quest", "Bare Quest");
        _steam.WriteLibraryList();
        var game = (await ScanAsync()).Games.Single();

        var added = await Games.AddLibraryGameAsync(game);

        Assert.Equal(LibraryAddStatus.Added, added.Value!.Status);
        Assert.Null((await Games.ListAsync(_workspace.Token)).Value!.Single().Launch.ProcessNameForExitDetection);
    }

    [Fact]
    public async Task A_scan_that_is_cancelled_ends_with_the_cancelled_error()
    {
        _steam.AddGame(_steam.SteamFolder, 240, "Counter-Strike: Source", "Counter-Strike Source");
        _steam.WriteLibraryList();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var scanned = await Games.ScanLibrariesAsync(cancelled.Token);

        Assert.Equal("error.operation.cancelled", scanned.Error?.MessageId);
    }

    [Fact]
    public async Task Reading_the_stores_does_not_hold_the_storage_gate_so_a_slow_drive_cannot_block_a_move()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var slow = new BlockingSteamFolder(_steam.SteamFolder, entered, release);
        _workspace.Dispose();
        _workspace = new BackendWorkspace(options => options with { SteamFolder = slow, GogInstalls = _gog }, _steam.SteamFolder);

        var scan = Games.ScanLibrariesAsync(_workspace.Token);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10), _workspace.Token), "The scan never started reading.");
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(_workspace.Token);
        limit.CancelAfter(TimeSpan.FromSeconds(5));
        (await _workspace.Backend.Services.Storage.Gate.EnterExclusiveAsync(limit.Token)).Dispose();
        release.Set();
        var done = await scan;

        Assert.True(done.Ok, done.Error?.Detail);
    }

    private sealed class BlockingSteamFolder(string folder, ManualResetEventSlim entered, ManualResetEventSlim release) : Savepoint.Core.Artwork.ISteamFolderLocator
    {
        public string? FindSteamFolder()
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(30));
            return folder;
        }
    }
}
