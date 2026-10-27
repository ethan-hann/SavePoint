using Savepoint.Core.Catalog;
using Savepoint.Core.Launch;
using Savepoint.Core.Platform;
using Savepoint.Core.Tests.Services;

namespace Savepoint.Core.Tests.Launch;

public class SteamGamesTests
{
    private static GameDefinition Game(LaunchMethod method = LaunchMethod.Exe, string exe = "Game.exe", int? steamAppId = null, string? uri = null) => new()
    {
        Id = "g",
        Name = "G",
        Install = new InstallLocation("/games/g"),
        Launch = new LaunchConfig(method, method == LaunchMethod.Exe ? exe : null, uri),
        SteamAppId = steamAppId,
    };

    private static bool Needs(GameDefinition game, string? install = null, OsKind os = OsKind.Windows) =>
        SteamGames.NeedsClient(game, install, os, StringComparison.OrdinalIgnoreCase);

    // A game folder in a Steam library, with the manifest Steam keeps for it.
    private static string SteamLibraryInstall(TempFolder library, int appId = 2379780, string folder = "Balatro")
    {
        var steamApps = Path.Combine(library.Path, "steamapps");
        var install = Path.Combine(steamApps, "common", folder);
        Directory.CreateDirectory(install);
        File.WriteAllText(Path.Combine(steamApps, $"appmanifest_{appId}.acf"), $"\"AppState\"\n{{\n\t\"installdir\"\t\t\"{folder}\"\n}}\n");
        return install;
    }

    [Fact]
    public void A_Steam_app_id_alone_does_not_make_a_game_a_Steam_game_when_the_install_is_outside_a_Steam_library()
    {
        // A GOG or retail copy gets the app id of the template it matched, only to find its artwork.
        using var folder = new TempFolder();

        Assert.False(Needs(Game(steamAppId: 2379780), folder.Path));
        Assert.False(Needs(Game(steamAppId: 2379780)));
    }

    [Fact]
    public void A_game_with_no_Steam_sign_at_all_does_not()
    {
        using var folder = new TempFolder();

        Assert.False(Needs(Game(), folder.Path));
    }

    [Fact]
    public void A_game_installed_in_a_Steam_library_needs_it_even_without_a_stored_app_id()
    {
        using var library = new TempFolder();

        Assert.True(Needs(Game(), SteamLibraryInstall(library)));
    }

    [Fact]
    public void A_game_installed_in_a_Steam_library_needs_it_when_its_program_sits_in_a_subfolder_of_the_install()
    {
        using var library = new TempFolder();
        var deeper = Path.Combine(SteamLibraryInstall(library), "bin");
        Directory.CreateDirectory(deeper);

        Assert.True(Needs(Game(), deeper));
    }

    [Fact]
    public void A_game_with_a_stray_Steam_app_id_file_outside_a_Steam_library_does_not()
    {
        // Retail and GOG builds ship a steam_appid.txt that names some other app.
        using var folder = new TempFolder();
        File.WriteAllText(Path.Combine(folder.Path, "steam_appid.txt"), "480");

        Assert.False(Needs(Game(), folder.Path));
    }

    [Fact]
    public void A_game_started_from_a_link_never_needs_it_because_Steam_starts_it()
    {
        using var library = new TempFolder();

        Assert.False(Needs(Game(LaunchMethod.Uri, uri: "steam://rungameid/2379780", steamAppId: 2379780), SteamLibraryInstall(library)));
    }

    [Theory]
    [InlineData(OsKind.Linux)]
    [InlineData(OsKind.MacOS)]
    public void A_Windows_program_on_an_OS_that_cannot_run_one_is_left_to_its_own_refusal(OsKind os)
    {
        using var library = new TempFolder();

        Assert.False(Needs(Game(exe: "Game.exe", steamAppId: 480), SteamLibraryInstall(library), os));
    }

    [Theory]
    [InlineData(OsKind.Linux)]
    [InlineData(OsKind.MacOS)]
    public void A_native_program_on_those_systems_needs_it(OsKind os)
    {
        using var library = new TempFolder();

        Assert.True(Needs(Game(exe: "game.x86_64", steamAppId: 480), SteamLibraryInstall(library), os));
    }
}
