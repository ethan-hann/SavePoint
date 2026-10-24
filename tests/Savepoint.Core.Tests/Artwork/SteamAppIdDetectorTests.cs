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
    [InlineData("\uFEFF489830", 489830)]
    // The endings Steam itself writes: 13 of the 25 real install-folder files end in a newline and a NUL byte, one in a space and CRLF.
    [InlineData("377160\n\0", 377160)]
    [InlineData("1551360 \r\n", 1551360)]
    [InlineData("\0\0 489830 \n\0\0", 489830)]
    [InlineData("489830\0", 489830)]
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
    [InlineData("\0")]
    [InlineData("\n\0")]
    [InlineData("48\09830")]
    [InlineData("489830\0 12")]
    [InlineData("489830\n\0\n489831\n\0")]
    [InlineData("48 9830")]
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
    public void The_launch_link_beats_the_manifest_and_the_manifest_beats_the_appid_file_inside_a_library()
    {
        using var folder = new TempFolder();
        var install = folder.Combine("lib", "steamapps", "common", "Skyrim");
        folder.WriteText("lib/steamapps/common/Skyrim/steam_appid.txt", "222");
        folder.WriteText("lib/steamapps/appmanifest_333.acf", Manifest("Skyrim"));

        Assert.Equal(333, Detect(FolderGame(install)));
        var linked = FolderGame(install) with { Launch = new LaunchConfig(LaunchMethod.Uri, Uri: "steam://run/111") };
        Assert.Equal(111, Detect(linked));
        File.Delete(Path.Combine(install, "steam_appid.txt"));
        Assert.Equal(333, Detect(FolderGame(install)));
    }

    // Mafia III: the file says 398870, which has no store page; the manifest says 360430.
    [Fact]
    public void A_file_that_names_an_app_with_no_store_page_loses_to_the_manifest_in_a_library()
    {
        using var folder = new TempFolder();
        folder.WriteText("lib/steamapps/common/Mafia III/steam_appid.txt", "398870");
        folder.WriteText("lib/steamapps/appmanifest_360430.acf", Manifest("Mafia III"));

        Assert.Equal(360430, Detect(FolderGame(folder.Combine("lib", "steamapps", "common", "Mafia III"))));
    }

    [Fact]
    public void The_appid_file_is_the_fallback_when_a_library_folder_has_no_matching_manifest()
    {
        using var folder = new TempFolder();
        folder.WriteText("lib/steamapps/common/Skyrim/steam_appid.txt", "222");
        folder.WriteText("lib/steamapps/appmanifest_333.acf", Manifest("Something Else"));

        Assert.Equal(222, Detect(FolderGame(folder.Combine("lib", "steamapps", "common", "Skyrim"))));
    }

    // Real shapes: Borderlands 2 keeps "49520" (no newline) in Binaries/Win32, Borderlands GOTY in Binaries/Win64, BioShock in Build/Final.
    [Theory]
    [InlineData("Binaries/Win32", "49520")]
    [InlineData("Binaries/Win64", "729040")]
    [InlineData("Binaries", "35140\r\n")]
    [InlineData("Build/Final", "409710")]
    [InlineData("x64", "356190")]
    [InlineData("bin", "431960")]
    [InlineData("Launcher", "8500")]
    [InlineData("Online/Binaries/Win32", "209000")]
    public void A_game_outside_a_library_with_the_appid_file_only_in_a_subfolder_names_the_app(string subfolder, string content)
    {
        using var folder = new TempFolder();
        folder.WriteText($"game/{subfolder}/steam_appid.txt", content);

        Assert.Equal(int.Parse(content.Trim(), System.Globalization.CultureInfo.InvariantCulture), Detect(FolderGame(folder.Combine("game"))));
    }

    [Fact]
    public void The_appid_file_in_the_install_folder_beats_one_in_a_subfolder()
    {
        using var folder = new TempFolder();
        folder.WriteText("game/steam_appid.txt", "200260\n\0");
        folder.WriteText("game/Binaries/Win32/steam_appid.txt", "999");

        Assert.Equal(200260, Detect(FolderGame(folder.Combine("game"))));
    }

    [Fact]
    public void A_shallower_subfolder_beats_a_deeper_one()
    {
        using var folder = new TempFolder();
        folder.WriteText("game/aaa/bbb/ccc/steam_appid.txt", "111");
        folder.WriteText("game/zzz/steam_appid.txt", "222");

        Assert.Equal(222, Detect(FolderGame(folder.Combine("game"))));
    }

    [Fact]
    public void A_subfolder_search_stops_three_levels_down()
    {
        using var folder = new TempFolder();
        folder.WriteText("game/a/b/c/steam_appid.txt", "111");
        folder.WriteText("other/a/b/c/d/steam_appid.txt", "222");

        Assert.Equal(111, Detect(FolderGame(folder.Combine("game"))));
        Assert.Null(Detect(FolderGame(folder.Combine("other"))));
    }

    [Fact]
    public void A_subfolder_search_gives_up_on_a_folder_with_a_great_many_subfolders()
    {
        using var folder = new TempFolder();
        for (var i = 0; i < 600; i++)
        {
            Directory.CreateDirectory(folder.Combine("game", $"d{i:D4}"));
        }

        folder.WriteText("game/d0599/steam_appid.txt", "111");

        Assert.Null(Detect(FolderGame(folder.Combine("game"))));
    }

    [Fact]
    public void A_subfolder_search_never_follows_a_link()
    {
        using var folder = new TempFolder();
        folder.WriteText("elsewhere/steam_appid.txt", "111");
        Directory.CreateDirectory(folder.Combine("game"));
        try
        {
            Directory.CreateSymbolicLink(folder.Combine("game", "link"), folder.Combine("elsewhere"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Assert.Skip("This account may not create symbolic links.");
        }

        Assert.Null(Detect(FolderGame(folder.Combine("game"))));
    }

    [Fact]
    public void A_subfolder_file_is_not_looked_for_when_the_manifest_already_answered()
    {
        using var folder = new TempFolder();
        folder.WriteText("lib/steamapps/common/House Flipper/Assets/Plugins/Windows_x64/steam_appid.txt", "480");
        folder.WriteText("lib/steamapps/common/House Flipper/steam_appid.txt", "1471280");
        folder.WriteText("lib/steamapps/appmanifest_613100.acf", Manifest("House Flipper"));

        Assert.Equal(613100, Detect(FolderGame(folder.Combine("lib", "steamapps", "common", "House Flipper"))));
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
