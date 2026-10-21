using Savepoint.Core.Artwork;
using Savepoint.Core.Catalog;
using Savepoint.Core.Tests.Services;

namespace Savepoint.Core.Tests.Artwork;

public class SteamAppIdDetectorTests
{
    private static GameDefinition LinkGame(string? uri, LaunchMethod method = LaunchMethod.Uri) => new()
    {
        Id = "g",
        Name = "G",
        Install = new InstallLocation("/games/g"),
        Launch = new LaunchConfig(method, ExePath: method == LaunchMethod.Exe ? "g.exe" : null, Uri: uri),
    };

    private static GameDefinition FolderGame(string install) => new()
    {
        Id = "g",
        Name = "G",
        Install = new InstallLocation(install),
        Launch = new LaunchConfig(LaunchMethod.Exe, "g.exe"),
    };

    private static int? Detect(GameDefinition game, string? install = null, StringComparison comparison = StringComparison.Ordinal) =>
        SteamAppIdDetector.Detect(game, install ?? game.Install.Path, comparison);

    private static string Manifest(string installDir) =>
        $"\"AppState\"\n{{\n\t\"appid\"\t\t\"1\"\n\t\"Universe\"\t\t\"1\"\n\t\"name\"\t\t\"A game\"\n\t\"installdir\"\t\t\"{installDir}\"\n\t\"LastUpdated\"\t\t\"1700000000\"\n}}\n";

    [Theory]
    [InlineData("steam://run/489830", 489830)]
    [InlineData("steam://rungameid/489830", 489830)]
    [InlineData("STEAM://RunGameId/489830", 489830)]
    [InlineData("  steam://run/489830  ", 489830)]
    [InlineData("steam://run/489830//-windowed", 489830)]
    [InlineData("steam://run/489830/", 489830)]
    [InlineData("steam://run/489830?x=1", 489830)]
    [InlineData("steam://rungameid/2147483647", 2147483647)]
    public void A_steam_launch_link_names_the_app(string link, int expected)
    {
        Assert.Equal(expected, Detect(LinkGame(link)));
    }

    [Theory]
    [InlineData("steam://run/abc")]
    [InlineData("steam://run/")]
    [InlineData("steam://run/0")]
    [InlineData("steam://run/-5")]
    [InlineData("steam://run/48983x")]
    [InlineData("steam://rungameid/12345678901234")]
    [InlineData("steam://open/games")]
    [InlineData("steam://store/489830")]
    [InlineData("https://store.steampowered.com/app/489830")]
    [InlineData("epic://launch/489830")]
    [InlineData("xsteam://run/489830")]
    [InlineData("")]
    public void A_link_that_is_not_a_steam_launch_link_with_a_usable_id_names_nothing(string link)
    {
        Assert.Null(Detect(LinkGame(link)));
    }

    [Fact]
    public void A_stale_link_on_a_game_that_starts_a_program_is_not_used()
    {
        var game = LinkGame("steam://run/489830", LaunchMethod.Exe) with { Launch = new LaunchConfig(LaunchMethod.Exe, "g.exe", Uri: "steam://run/489830") };

        Assert.Null(Detect(game));
    }

    [Theory]
    [InlineData("489830", 489830)]
    [InlineData("489830\n", 489830)]
    [InlineData("489830\r\n", 489830)]
    [InlineData("  489830  ", 489830)]
    [InlineData("﻿489830", 489830)]
    public void A_steam_appid_file_holding_only_digits_names_the_app(string content, int expected)
    {
        using var folder = new TempFolder();
        folder.WriteText("game/steam_appid.txt", content);

        Assert.Equal(expected, Detect(FolderGame(folder.Combine("game"))));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("abc")]
    [InlineData("489830 and more")]
    [InlineData("489830\n489831")]
    [InlineData("0")]
    [InlineData("-480")]
    [InlineData("99999999999")]
    [InlineData("4,898")]
    public void A_steam_appid_file_holding_anything_else_names_nothing(string content)
    {
        using var folder = new TempFolder();
        folder.WriteText("game/steam_appid.txt", content);

        Assert.Null(Detect(FolderGame(folder.Combine("game"))));
    }

    [Fact]
    public void A_steam_appid_file_that_is_far_too_big_is_not_read()
    {
        using var folder = new TempFolder();
        folder.WriteText("game/steam_appid.txt", new string('1', 5000));

        Assert.Null(Detect(FolderGame(folder.Combine("game"))));
    }

    [Fact]
    public void No_steam_appid_file_and_no_manifest_names_nothing_and_a_missing_install_folder_does_not_throw()
    {
        using var folder = new TempFolder();
        Directory.CreateDirectory(folder.Combine("game"));

        Assert.Null(Detect(FolderGame(folder.Combine("game"))));
        Assert.Null(Detect(FolderGame(folder.Combine("gone"))));
        Assert.Null(Detect(FolderGame(string.Empty), install: string.Empty));
        Assert.Null(SteamAppIdDetector.Detect(FolderGame("{documents}/x"), null, StringComparison.Ordinal));
    }

    [Fact]
    public void The_manifest_that_names_the_install_folder_gives_the_app()
    {
        using var folder = new TempFolder();
        folder.WriteText("lib/steamapps/common/Skyrim Special Edition/SkyrimSE.exe", "x");
        folder.WriteText("lib/steamapps/appmanifest_489830.acf", Manifest("Skyrim Special Edition"));
        folder.WriteText("lib/steamapps/appmanifest_377160.acf", Manifest("Fallout 4"));

        Assert.Equal(489830, Detect(FolderGame(folder.Combine("lib", "steamapps", "common", "Skyrim Special Edition"))));
    }

    [Fact]
    public void An_install_folder_inside_the_game_s_folder_still_finds_the_manifest()
    {
        using var folder = new TempFolder();
        folder.WriteText("lib/steamapps/common/Skyrim/Data/a.esp", "x");
        folder.WriteText("lib/steamapps/appmanifest_72850.acf", Manifest("Skyrim"));

        Assert.Equal(72850, Detect(FolderGame(folder.Combine("lib", "steamapps", "common", "Skyrim", "Data"))));
    }

    [Fact]
    public void A_manifest_matches_without_regard_to_case_only_where_paths_ignore_case()
    {
        using var folder = new TempFolder();
        folder.WriteText("lib/steamapps/common/Skyrim/a.txt", "x");
        folder.WriteText("lib/steamapps/appmanifest_72850.acf", Manifest("skyrim"));
        var game = FolderGame(folder.Combine("lib", "steamapps", "common", "Skyrim"));

        Assert.Equal(72850, Detect(game, comparison: StringComparison.OrdinalIgnoreCase));
        Assert.Null(Detect(game, comparison: StringComparison.Ordinal));
    }

    [Fact]
    public void A_manifest_for_another_folder_or_one_with_no_install_folder_names_nothing()
    {
        using var folder = new TempFolder();
        folder.WriteText("lib/steamapps/common/Skyrim/a.txt", "x");
        folder.WriteText("lib/steamapps/appmanifest_1.acf", Manifest("Skyrim Special Edition"));
        folder.WriteText("lib/steamapps/appmanifest_2.acf", "\"AppState\"\n{\n\t\"appid\"\t\t\"2\"\n}\n");
        folder.WriteText("lib/steamapps/appmanifest_x.acf", Manifest("Skyrim"));
        folder.WriteText("lib/steamapps/notamanifest.acf", Manifest("Skyrim"));

        Assert.Null(Detect(FolderGame(folder.Combine("lib", "steamapps", "common", "Skyrim"))));
    }

    [Fact]
    public void An_install_folder_outside_steamapps_common_is_not_matched_to_a_manifest()
    {
        using var folder = new TempFolder();
        folder.WriteText("lib/steamapps/appmanifest_1.acf", Manifest("Skyrim"));
        folder.WriteText("lib/games/Skyrim/a.txt", "x");
        folder.WriteText("lib/steamapps/other/Skyrim/a.txt", "x");
        folder.WriteText("lib/common/Skyrim/a.txt", "x");

        Assert.Null(Detect(FolderGame(folder.Combine("lib", "games", "Skyrim"))));
        Assert.Null(Detect(FolderGame(folder.Combine("lib", "steamapps", "other", "Skyrim"))));
        Assert.Null(Detect(FolderGame(folder.Combine("lib", "common", "Skyrim"))));
    }

    [Fact]
    public void Several_manifests_for_one_folder_give_the_lowest_app_id()
    {
        using var folder = new TempFolder();
        folder.WriteText("lib/steamapps/common/Shared/a.txt", "x");
        folder.WriteText("lib/steamapps/appmanifest_900.acf", Manifest("Shared"));
        folder.WriteText("lib/steamapps/appmanifest_700.acf", Manifest("Shared"));

        Assert.Equal(700, Detect(FolderGame(folder.Combine("lib", "steamapps", "common", "Shared"))));
    }

    [Fact]
    public void The_launch_link_beats_the_appid_file_and_the_appid_file_beats_the_manifest()
    {
        using var folder = new TempFolder();
        var install = folder.Combine("lib", "steamapps", "common", "Skyrim");
        folder.WriteText("lib/steamapps/common/Skyrim/steam_appid.txt", "222");
        folder.WriteText("lib/steamapps/appmanifest_333.acf", Manifest("Skyrim"));

        Assert.Equal(222, Detect(FolderGame(install)));
        var linked = FolderGame(install) with { Launch = new LaunchConfig(LaunchMethod.Uri, Uri: "steam://run/111") };
        Assert.Equal(111, Detect(linked));
        File.Delete(Path.Combine(install, "steam_appid.txt"));
        Assert.Equal(333, Detect(FolderGame(install)));
    }

    [Fact]
    public void A_link_without_a_usable_id_falls_through_to_the_folder_checks()
    {
        using var folder = new TempFolder();
        folder.WriteText("game/steam_appid.txt", "480");
        var game = FolderGame(folder.Combine("game")) with { Launch = new LaunchConfig(LaunchMethod.Uri, Uri: "steam://open/games") };

        Assert.Equal(480, Detect(game));
    }

    [Fact]
    public void Detecting_reads_files_and_leaves_every_one_of_them_as_it_was()
    {
        using var folder = new TempFolder();
        folder.WriteText("lib/steamapps/common/Skyrim/steam_appid.txt", "222");
        folder.WriteText("lib/steamapps/appmanifest_333.acf", Manifest("Skyrim"));
        var before = Snapshot(folder.Path);

        Detect(FolderGame(folder.Combine("lib", "steamapps", "common", "Skyrim")));

        Assert.Equal(before, Snapshot(folder.Path));
    }

    private static Dictionary<string, (long Length, DateTime Written)> Snapshot(string root) =>
        Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, path => (File.Exists(path) ? new FileInfo(path).Length : -1L, File.GetLastWriteTimeUtc(path)));
}
