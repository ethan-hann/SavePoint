using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using static Savepoint.Core.Tests.Catalog.CatalogSamples;

namespace Savepoint.Core.Tests.Catalog;

public class GameValidationTests
{
    public static TheoryData<string, string> NestedPaths => new()
    {
        { @"C:\Games\Skyrim", @"C:\Games\Skyrim" },
        { @"C:\Games\Skyrim", @"C:\Games\Skyrim\Saves" },
        { @"C:\Games\Skyrim\Saves", @"C:\Games\Skyrim" },
        { @"C:\GAMES\skyrim", "c:/games/Skyrim/saves" },
        { @"C:\Games\Skyrim\", @"C:\Games\Skyrim" },
        { "{documents}/Game", "{documents}/Game/Saves" },
        { "/games/a", "/games/a/b/c" },
    };

    public static TheoryData<string, string> SeparatePaths => new()
    {
        { @"C:\Games\Skyrim", @"C:\Games\SkyrimSaves" },
        { @"C:\Games", @"D:\Games" },
        { "{documents}/Game", "{documents}/Game2" },
        { "/games/a", "/games/b" },
    };

    [Fact]
    public void Valid_exe_game_has_no_issues()
    {
        Assert.Empty(CatalogValidator.Validate(ExeGame()));
    }

    [Fact]
    public void Valid_uri_game_without_saves_folder_has_no_issues()
    {
        Assert.Empty(CatalogValidator.Validate(UriGame()));
    }

    [Fact]
    public void Wrong_schema_version_is_reported()
    {
        var issues = CatalogValidator.Validate(ExeGame() with { SchemaVersion = 2 });

        Assert.Equal([Issue("schemaVersion", ValidationMessageIds.GameSchemaVersionUnsupported)], issues);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_id_is_reported(string id)
    {
        var issues = CatalogValidator.Validate(ExeGame(id));

        Assert.Equal([Issue("id", ValidationMessageIds.GameIdRequired)], issues);
    }

    [Theory]
    [InlineData("../x")]
    [InlineData("a/b")]
    [InlineData(@"a\b")]
    [InlineData("a b")]
    [InlineData("a.b")]
    [InlineData("-leading")]
    [InlineData("tr\u00e9s")]
    public void Id_that_is_not_a_safe_folder_name_is_reported(string id)
    {
        var issues = CatalogValidator.Validate(ExeGame(id));

        Assert.Equal([Issue("id", ValidationMessageIds.GameIdInvalid)], issues);
    }

    [Theory]
    [InlineData("skyrim-se")]
    [InlineData("a")]
    [InlineData("Game_2-b")]
    public void Ordinary_ids_are_accepted(string id)
    {
        Assert.Empty(CatalogValidator.Validate(ExeGame(id)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \t")]
    public void Blank_name_is_reported(string name)
    {
        var issues = CatalogValidator.Validate(ExeGame() with { Name = name });

        Assert.Equal([Issue("name", ValidationMessageIds.GameNameRequired)], issues);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Blank_install_path_is_reported(string path)
    {
        var game = ExeGame() with { Install = new InstallLocation(path), External = null };

        Assert.Equal([Issue("install.path", ValidationMessageIds.GameInstallPathRequired)], CatalogValidator.Validate(game));
    }

    [Fact]
    public void Blank_saves_path_is_reported_when_a_saves_folder_is_present()
    {
        var game = ExeGame() with { External = new ExternalLocation(" ") };

        Assert.Equal([Issue("external.path", ValidationMessageIds.GameExternalPathRequired)], CatalogValidator.Validate(game));
    }

    [Fact]
    public void Unknown_saves_role_is_reported()
    {
        var game = ExeGame() with { External = new ExternalLocation(@"D:\Saves", (ExternalRole)99) };

        Assert.Equal([Issue("external.role", ValidationMessageIds.GameExternalRoleInvalid)], CatalogValidator.Validate(game));
    }

    [Theory]
    [MemberData(nameof(NestedPaths))]
    public void Install_and_saves_paths_that_nest_are_reported_on_the_saves_path(string install, string external)
    {
        var game = ExeGame() with { Install = new InstallLocation(install), External = new ExternalLocation(external) };

        Assert.Equal([Issue("external.path", ValidationMessageIds.GamePathsNested)], CatalogValidator.Validate(game));
    }

    [Theory]
    [MemberData(nameof(SeparatePaths))]
    public void Install_and_saves_paths_that_only_share_a_text_prefix_are_accepted(string install, string external)
    {
        var game = ExeGame() with { Install = new InstallLocation(install), External = new ExternalLocation(external) };

        Assert.Empty(CatalogValidator.Validate(game));
    }

    [Fact]
    public void Unknown_launch_method_is_reported()
    {
        var game = ExeGame() with { Launch = new LaunchConfig((LaunchMethod)99, ExePath: "g.exe") };

        Assert.Equal([Issue("launch.method", ValidationMessageIds.LaunchMethodInvalid)], CatalogValidator.Validate(game));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Exe_launch_without_an_exe_path_is_reported(string? exePath)
    {
        var game = ExeGame() with { Launch = new LaunchConfig(LaunchMethod.Exe, ExePath: exePath) };

        Assert.Equal([Issue("launch.exePath", ValidationMessageIds.LaunchExePathRequired)], CatalogValidator.Validate(game));
    }

    [Theory]
    [InlineData(@"C:\Games\Game.exe")]
    [InlineData("C:Game.exe")]
    [InlineData("c:/Games/Game.exe")]
    [InlineData("/usr/bin/game")]
    [InlineData(@"\Game.exe")]
    [InlineData(@"\\server\share\Game.exe")]
    public void Absolute_exe_path_is_reported(string exePath)
    {
        var game = ExeGame() with { Launch = new LaunchConfig(LaunchMethod.Exe, ExePath: exePath) };

        Assert.Equal([Issue("launch.exePath", ValidationMessageIds.LaunchExePathAbsolute)], CatalogValidator.Validate(game));
    }

    [Theory]
    [InlineData(@"..\evil.exe")]
    [InlineData("../evil.exe")]
    [InlineData("bin/../../evil.exe")]
    [InlineData(@"bin\..\..\evil.exe")]
    [InlineData("bin/../game.exe")]
    [InlineData("..")]
    public void Exe_path_that_climbs_out_of_the_install_folder_is_reported(string exePath)
    {
        var game = ExeGame() with { Launch = new LaunchConfig(LaunchMethod.Exe, ExePath: exePath) };

        Assert.Equal([Issue("launch.exePath", ValidationMessageIds.LaunchExePathEscapes)], CatalogValidator.Validate(game));
    }

    [Theory]
    [InlineData("Game.exe")]
    [InlineData(@"bin\Win64\Game.exe")]
    [InlineData("bin/Game.exe")]
    [InlineData("./Game.exe")]
    [InlineData("Game..exe")]
    [InlineData("Game.app/Contents/MacOS/Game")]
    public void Relative_exe_paths_inside_the_install_folder_are_accepted(string exePath)
    {
        var game = ExeGame() with { Launch = new LaunchConfig(LaunchMethod.Exe, ExePath: exePath) };

        Assert.Empty(CatalogValidator.Validate(game));
    }

    [Fact]
    public void Exe_path_is_not_checked_when_the_game_starts_through_a_uri()
    {
        var game = UriGame() with { Launch = new LaunchConfig(LaunchMethod.Uri, ExePath: @"C:\old\Game.exe", Uri: "steam://run/1") };

        Assert.Empty(CatalogValidator.Validate(game));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Uri_launch_without_a_uri_is_reported(string? uri)
    {
        var game = UriGame() with { Launch = new LaunchConfig(LaunchMethod.Uri, Uri: uri) };

        Assert.Equal([Issue("launch.uri", ValidationMessageIds.LaunchUriRequired)], CatalogValidator.Validate(game));
    }

    [Theory]
    [InlineData("run/489830")]
    [InlineData(@"C:\Games\Game.exe")]
    [InlineData("/usr/bin/game")]
    [InlineData("steam:")]
    [InlineData("://run/1")]
    [InlineData("1steam://run/1")]
    public void Uri_without_a_scheme_is_reported(string uri)
    {
        var game = UriGame() with { Launch = new LaunchConfig(LaunchMethod.Uri, Uri: uri) };

        Assert.Equal([Issue("launch.uri", ValidationMessageIds.LaunchUriInvalid)], CatalogValidator.Validate(game));
    }

    [Theory]
    [InlineData("steam://run/489830")]
    [InlineData("com.epicgames.launcher://apps/abc?action=launch")]
    [InlineData("goggalaxy://openGameView/1")]
    public void Uris_with_a_scheme_are_accepted(string uri)
    {
        var game = UriGame() with { Launch = new LaunchConfig(LaunchMethod.Uri, Uri: uri) };

        Assert.Empty(CatalogValidator.Validate(game));
    }

    [Fact]
    public void Uri_is_not_checked_when_the_game_starts_through_an_exe()
    {
        var game = ExeGame() with { Launch = new LaunchConfig(LaunchMethod.Exe, ExePath: "g.exe", Uri: "not a uri") };

        Assert.Empty(CatalogValidator.Validate(game));
    }

    [Fact]
    public void Blank_exit_detection_name_counts_as_absent()
    {
        var game = ExeGame() with { Launch = new LaunchConfig(LaunchMethod.Exe, ExePath: "g.exe", ProcessNameForExitDetection: "  ") };

        Assert.Empty(CatalogValidator.Validate(game));
        Assert.Null(CatalogValidator.Normalize(game).Launch.ProcessNameForExitDetection);
    }

    [Fact]
    public void Every_problem_is_reported_at_once_in_field_order()
    {
        var game = new GameDefinition(
            SchemaVersion: 3,
            Id: "",
            Name: "  ",
            Install: new InstallLocation(""),
            External: new ExternalLocation("", (ExternalRole)7),
            Launch: new LaunchConfig(LaunchMethod.Exe, ExePath: "../x.exe"));

        var issues = CatalogValidator.Validate(game);

        Assert.Equal(
            [
                Issue("schemaVersion", ValidationMessageIds.GameSchemaVersionUnsupported),
                Issue("id", ValidationMessageIds.GameIdRequired),
                Issue("name", ValidationMessageIds.GameNameRequired),
                Issue("install.path", ValidationMessageIds.GameInstallPathRequired),
                Issue("external.path", ValidationMessageIds.GameExternalPathRequired),
                Issue("external.role", ValidationMessageIds.GameExternalRoleInvalid),
                Issue("launch.exePath", ValidationMessageIds.LaunchExePathEscapes),
            ],
            issues);
    }

    [Fact]
    public void Nesting_is_reported_alongside_launch_problems()
    {
        var game = ExeGame() with
        {
            Install = new InstallLocation(@"C:\G"),
            External = new ExternalLocation(@"C:\G\saves"),
            Launch = new LaunchConfig(LaunchMethod.Uri),
        };

        Assert.Equal(
            [
                Issue("external.path", ValidationMessageIds.GamePathsNested),
                Issue("launch.uri", ValidationMessageIds.LaunchUriRequired),
            ],
            CatalogValidator.Validate(game));
    }

    [Fact]
    public void Normalize_trims_names_and_paths()
    {
        var game = ExeGame() with
        {
            Name = "  Skyrim  ",
            Install = new InstallLocation("  C:\\Games\\SkyrimSE "),
            External = new ExternalLocation(" D:\\Saves  "),
            Launch = new LaunchConfig(LaunchMethod.Exe, ExePath: " g.exe ", ProcessNameForExitDetection: " S.exe "),
        };

        var normalized = CatalogValidator.Normalize(game);

        Assert.Equal("Skyrim", normalized.Name);
        Assert.Equal(@"C:\Games\SkyrimSE", normalized.Install.Path);
        Assert.Equal(@"D:\Saves", normalized.External!.Path);
        Assert.Equal("g.exe", normalized.Launch.ExePath);
        Assert.Equal("S.exe", normalized.Launch.ProcessNameForExitDetection);
    }

    [Fact]
    public void Normalize_trims_ignore_and_mod_hint_entries_and_drops_blanks_and_duplicates_in_order()
    {
        var game = ExeGame() with
        {
            IgnorePaths = [" **/*.log ", "", "  ", "**/*.log", "Saves/", " Saves/"],
            ModPathHints = ["Data", " Data", "plugins.txt", "\t"],
        };

        var normalized = CatalogValidator.Normalize(game);

        Assert.Equal(["**/*.log", "Saves/"], normalized.IgnorePaths);
        Assert.Equal(["Data", "plugins.txt"], normalized.ModPathHints);
    }

    [Fact]
    public void Normalize_leaves_launch_arguments_and_notes_untouched()
    {
        var game = ExeGame() with
        {
            Launch = new LaunchConfig(LaunchMethod.Exe, ExePath: "g.exe", Args: [" -a ", "", " "]),
            Notes = "  keep my spacing \n",
        };

        var normalized = CatalogValidator.Normalize(game);

        Assert.Equal([" -a ", "", " "], normalized.Launch.Args);
        Assert.Equal("  keep my spacing \n", normalized.Notes);
    }

    [Fact]
    public void Normalize_does_not_change_the_input()
    {
        var game = ExeGame() with { Name = "  Skyrim ", IgnorePaths = [" a ", "a"] };

        _ = CatalogValidator.Normalize(game);

        Assert.Equal("  Skyrim ", game.Name);
        Assert.Equal([" a ", "a"], game.IgnorePaths);
    }

    [Fact]
    public void Whitespace_only_name_fails_after_trimming()
    {
        var issues = CatalogValidator.Validate(ExeGame() with { Name = " \t " });

        Assert.Contains(Issue("name", ValidationMessageIds.GameNameRequired), issues);
    }

    [Fact]
    public void RequireValid_returns_the_normalized_game()
    {
        var game = ExeGame() with { Name = " Skyrim ", IgnorePaths = ["a", " a"] };

        var result = CatalogValidator.RequireValid(game);

        Assert.Equal("Skyrim", result.Name);
        Assert.Equal(["a"], result.IgnorePaths);
    }

    [Fact]
    public void RequireValid_throws_with_every_issue_for_an_invalid_game()
    {
        var game = ExeGame() with { Name = "", Launch = new LaunchConfig(LaunchMethod.Exe) };

        var error = Assert.Throws<ValidationException>(() => CatalogValidator.RequireValid(game));

        Assert.Equal("error.validation.invalidPayload", error.MessageId);
        Assert.Equal(
            [
                Issue("name", ValidationMessageIds.GameNameRequired),
                Issue("launch.exePath", ValidationMessageIds.LaunchExePathRequired),
            ],
            (IReadOnlyList<ValidationIssue>)error.Args["issues"]!);
    }

    [Fact]
    public void Validate_rejects_a_missing_argument()
    {
        Assert.Throws<ArgumentNullException>(() => CatalogValidator.Validate((GameDefinition)null!));
    }
}
