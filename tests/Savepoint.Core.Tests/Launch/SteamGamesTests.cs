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

    [Fact]
    public void A_game_with_a_Steam_app_id_started_from_its_program_needs_the_client()
    {
        Assert.True(Needs(Game(steamAppId: 2379780)));
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
        var steamApps = Path.Combine(library.Path, "steamapps");
        var install = Path.Combine(steamApps, "common", "Balatro");
        Directory.CreateDirectory(install);
        File.WriteAllText(Path.Combine(steamApps, "appmanifest_2379780.acf"), "\"AppState\"\n{\n\t\"installdir\"\t\t\"Balatro\"\n}\n");

        Assert.True(Needs(Game(), install));
    }

    [Fact]
    public void A_game_that_ships_a_Steam_app_id_file_needs_it()
    {
        using var folder = new TempFolder();
        File.WriteAllText(Path.Combine(folder.Path, "steam_appid.txt"), "480");

        Assert.True(Needs(Game(), folder.Path));
    }

    [Fact]
    public void A_game_started_from_a_link_never_needs_it_because_Steam_starts_it()
    {
        Assert.False(Needs(Game(LaunchMethod.Uri, uri: "steam://rungameid/2379780", steamAppId: 2379780)));
    }

    [Theory]
    [InlineData(OsKind.Linux)]
    [InlineData(OsKind.MacOS)]
    public void A_Windows_program_on_an_OS_that_cannot_run_one_is_left_to_its_own_refusal(OsKind os)
    {
        Assert.False(Needs(Game(exe: "Game.exe", steamAppId: 480), os: os));
    }

    [Theory]
    [InlineData(OsKind.Linux)]
    [InlineData(OsKind.MacOS)]
    public void A_native_program_on_those_systems_needs_it(OsKind os)
    {
        Assert.True(Needs(Game(exe: "game.x86_64", steamAppId: 480), os: os));
    }
}
