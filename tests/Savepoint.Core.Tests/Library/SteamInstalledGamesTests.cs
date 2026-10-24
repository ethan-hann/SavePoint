using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Library;

namespace Savepoint.Core.Tests.Library;

public class SteamInstalledGamesTests
{
    private static IReadOnlyList<InstalledGame> Scan(SteamLibraryRig rig) => SteamInstalledGames.Scan(rig.SteamFolder, LibraryFixtures.Syntax);

    [Fact]
    public void Games_in_the_steam_folder_and_in_a_second_library_are_all_listed()
    {
        using var rig = new SteamLibraryRig();
        var skyrim = rig.AddGame(rig.SteamFolder, 489830, "The Elder Scrolls V: Skyrim Special Edition", "Skyrim Special Edition");
        var strike = rig.AddGame(rig.AddLibrary(), 240, "Counter-Strike: Source", "Counter-Strike Source");
        rig.WriteLibraryList();

        var games = Scan(rig);

        Assert.Equal(["240", "489830"], games.Select(g => g.StoreId).Order().ToList());
        var found = games.Single(g => g.StoreId == "489830");
        Assert.Equal(LibrarySource.Steam, found.Source);
        Assert.Equal("The Elder Scrolls V: Skyrim Special Edition", found.Name);
        Assert.Equal(skyrim, found.InstallPath);
        Assert.Equal("Skyrim Special Edition", found.FolderName);
        Assert.Equal(489830, found.SteamAppId);
        Assert.Equal(strike, games.Single(g => g.StoreId == "240").InstallPath);
    }

    [Fact]
    public void A_game_starts_through_its_steam_link_so_it_can_be_added_without_a_template()
    {
        using var rig = new SteamLibraryRig();
        rig.AddGame(rig.SteamFolder, 240, "Counter-Strike: Source", "Counter-Strike Source");
        rig.WriteLibraryList();

        var launch = Scan(rig).Single().Launch;

        Assert.Equal(LaunchMethod.Uri, launch.Method);
        Assert.Equal("steam://rungameid/240", launch.Uri);
    }

    [Fact]
    public void The_whole_real_manifest_of_a_game_is_read()
    {
        using var rig = new SteamLibraryRig();
        File.WriteAllText(Path.Combine(rig.SteamFolder, "steamapps", "appmanifest_240.acf"), LibraryFixtures.CounterStrikeSourceManifest);
        Directory.CreateDirectory(Path.Combine(rig.SteamFolder, "steamapps", "common", "Counter-Strike Source"));
        rig.WriteLibraryList();

        var game = Assert.Single(Scan(rig));

        Assert.Equal(("240", "Counter-Strike: Source", "Counter-Strike Source"), (game.StoreId, game.Name, game.FolderName));
    }

    [Theory]
    [InlineData(228980, "Steamworks Common Redistributables", "Steamworks Shared")]
    [InlineData(250820, "SteamVR", "SteamVR")]
    [InlineData(1493710, LibraryFixtures.ProtonName, "Proton 9.0 (Beta)")]
    [InlineData(1628350, LibraryFixtures.LinuxRuntimeName, "SteamLinuxRuntime_sniper")]
    public void Steam_tools_are_not_games(int appId, string name, string installDir)
    {
        using var rig = new SteamLibraryRig();
        rig.AddGame(rig.SteamFolder, appId, name, installDir);
        rig.AddGame(rig.SteamFolder, 240, "Counter-Strike: Source", "Counter-Strike Source");
        rig.WriteLibraryList();

        var games = Scan(rig);

        Assert.Equal(["240"], games.Select(g => g.StoreId));
    }

    [Fact]
    public void A_manifest_whose_game_folder_is_gone_is_not_listed()
    {
        using var rig = new SteamLibraryRig();
        rig.AddGame(rig.SteamFolder, 240, "Counter-Strike: Source", "Counter-Strike Source", installed: false);
        rig.WriteLibraryList();

        Assert.Empty(Scan(rig));
    }

    [Theory]
    [InlineData(518)]
    [InlineData(22)]
    public void A_game_that_is_updating_is_still_installed(int stateFlags)
    {
        using var rig = new SteamLibraryRig();
        rig.AddGame(rig.SteamFolder, 1716740, "Starfield", "Starfield", stateFlags);
        rig.WriteLibraryList();

        Assert.Equal(["1716740"], Scan(rig).Select(g => g.StoreId));
    }

    [Fact]
    public void The_install_folder_comes_from_installdir_not_from_the_name()
    {
        using var rig = new SteamLibraryRig();
        var folder = rig.AddGame(rig.SteamFolder, 1783560, "The Last Caretaker", "Voyage");
        rig.WriteLibraryList();

        var game = Assert.Single(Scan(rig));

        Assert.Equal("The Last Caretaker", game.Name);
        Assert.Equal(folder, game.InstallPath);
    }

    [Fact]
    public void A_game_listed_in_two_libraries_is_listed_once()
    {
        using var rig = new SteamLibraryRig();
        rig.AddGame(rig.SteamFolder, 240, "Counter-Strike: Source", "Counter-Strike Source");
        rig.AddGame(rig.AddLibrary(), 240, "Counter-Strike: Source", "Counter-Strike Source");
        rig.WriteLibraryList();

        Assert.Single(Scan(rig));
    }

    [Fact]
    public void Apps_that_share_one_install_folder_are_listed_once_under_the_lowest_app_id()
    {
        // On the PC the manifests came from, Fallout 4 and its Creation Kit both install into "Fallout 4", and so do the two Modern Warfare 2 apps.
        using var rig = new SteamLibraryRig();
        rig.AddGame(rig.SteamFolder, 1946160, "Fallout 4: Creation Kit", "Fallout 4");
        rig.AddGame(rig.SteamFolder, 377160, "Fallout 4", "Fallout 4");
        rig.AddGame(rig.SteamFolder, 10190, "Call of Duty: Modern Warfare 2 (2009) - Multiplayer", "Call of Duty Modern Warfare 2");
        rig.AddGame(rig.SteamFolder, 10180, "Call of Duty: Modern Warfare 2 (2009)", "Call of Duty Modern Warfare 2");
        rig.WriteLibraryList();

        var games = Scan(rig);

        Assert.Equal(["10180", "377160"], games.Select(g => g.StoreId).Order(StringComparer.Ordinal).ToList());
    }

    [Fact]
    public void A_library_on_a_drive_that_is_not_connected_is_skipped_and_the_rest_are_listed()
    {
        using var rig = new SteamLibraryRig();
        rig.AddGame(rig.SteamFolder, 240, "Counter-Strike: Source", "Counter-Strike Source");
        rig.AddLibrary("OfflineLibrary", create: false);
        rig.WriteLibraryList();

        Assert.Equal(["240"], Scan(rig).Select(g => g.StoreId));
    }

    [Fact]
    public void Without_a_library_list_the_steam_folders_own_library_is_still_scanned()
    {
        using var rig = new SteamLibraryRig();
        rig.AddGame(rig.SteamFolder, 240, "Counter-Strike: Source", "Counter-Strike Source");

        Assert.Equal(["240"], Scan(rig).Select(g => g.StoreId));
    }

    [Fact]
    public void A_manifest_that_cannot_be_read_does_not_hide_the_others()
    {
        using var rig = new SteamLibraryRig();
        rig.AddGame(rig.SteamFolder, 240, "Counter-Strike: Source", "Counter-Strike Source");
        File.WriteAllText(Path.Combine(rig.SteamFolder, "steamapps", "appmanifest_999.acf"), "\"AppState\"\n{\n\t\"appid\"\t\t\"999");
        File.WriteAllText(Path.Combine(rig.SteamFolder, "steamapps", "appmanifest_998.acf"), LibraryFixtures.Manifest(998, "No Folder Name", "", 4));
        rig.WriteLibraryList();

        Assert.Equal(["240"], Scan(rig).Select(g => g.StoreId));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("..\\..\\Windows")]
    [InlineData("../outside")]
    [InlineData("C:\\Windows")]
    [InlineData("/etc")]
    public void An_install_folder_that_leaves_the_library_is_refused(string installDir)
    {
        using var rig = new SteamLibraryRig();
        rig.AddGame(rig.SteamFolder, 4000, "Odd Game", "Odd Game", installed: false);
        File.WriteAllText(
            Path.Combine(rig.SteamFolder, "steamapps", "appmanifest_4001.acf"),
            LibraryFixtures.Manifest(4001, "Hostile Game", installDir.Replace("\\", "\\\\"), 4));
        rig.WriteLibraryList();

        Assert.Empty(Scan(rig));
    }

    [Fact]
    public void A_missing_steam_folder_lists_nothing()
    {
        var games = SteamInstalledGames.Scan(Path.Combine(Path.GetTempPath(), "savepoint-no-such-steam-" + Guid.NewGuid().ToString("N")), LibraryFixtures.Syntax);

        Assert.Empty(games);
    }

    [Fact]
    public void Games_come_back_sorted_by_name()
    {
        using var rig = new SteamLibraryRig();
        rig.AddGame(rig.SteamFolder, 3, "Zebra Racing", "Zebra");
        rig.AddGame(rig.SteamFolder, 2, "alpha Quest", "Alpha");
        rig.AddGame(rig.SteamFolder, 1, "Beta Quest", "Beta");
        rig.WriteLibraryList();

        Assert.Equal(["alpha Quest", "Beta Quest", "Zebra Racing"], Scan(rig).Select(g => g.Name));
    }
}
