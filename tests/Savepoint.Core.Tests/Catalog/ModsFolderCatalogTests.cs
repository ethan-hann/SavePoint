using System.Text.Json;
using Savepoint.Core.Catalog;
using static Savepoint.Core.Tests.Catalog.CatalogSamples;

namespace Savepoint.Core.Tests.Catalog;

// The optional third folder: the mods folder outside the install.
public class ModsFolderCatalogTests : CatalogStoreTestBase
{
    // A games.json as the 0.9.16 build wrote it, with no mods key anywhere.
    private const string _v0916Games = """
        [
          {
            "schemaVersion": 1,
            "id": "skyrim-se",
            "name": "Skyrim",
            "install": { "path": "C:\\Games\\SkyrimSE" },
            "external": { "path": "{documents}\\My Games\\Skyrim Special Edition\\Saves", "role": "saves" },
            "launch": { "method": "exe", "exePath": "SkyrimSELauncher.exe", "args": [], "processNameForExitDetection": "SkyrimSE.exe" },
            "ignorePaths": ["**/*.log"],
            "modPathHints": ["Data"],
            "installIgnorePaths": ["**/*.dmp"]
          }
        ]
        """;

    private static GameDefinition WithMods(string install, string? saves, string mods) =>
        ExeGame() with
        {
            Install = new InstallLocation(install),
            External = saves is null ? null : new ExternalLocation(saves),
            Mods = new ModsLocation(mods),
        };

    public static TheoryData<string, string?, string, string> NestedFolders => new()
    {
        { @"C:\Games\Game", null, @"C:\Games\Game", "install and mods are the same folder" },
        { @"C:\Games\Game", null, @"C:\Games\Game\Mods", "mods inside the install" },
        { @"C:\Games\Game\Mods", null, @"C:\Games\Game", "install inside the mods folder" },
        { @"C:\Games\Game", @"D:\Saves", @"D:\Saves", "saves and mods are the same folder" },
        { @"C:\Games\Game", @"D:\Data", @"D:\Data\Mods", "mods inside the saves" },
        { @"C:\Games\Game", @"D:\Data\Saves", @"D:\Data", "saves inside the mods folder" },
        { @"C:\GAMES\Game", @"D:\Data", "d:/data/MODS", "mods inside the saves with other letter case and slashes" },
    };

    [Fact]
    public void A_game_with_a_mods_folder_reads_it_back_from_its_json()
    {
        var game = ExeGame() with { Mods = new ModsLocation(@"{appData}\Game\Mods") };

        var json = JsonSerializer.Serialize(game, CatalogJsonContext.Default.GameDefinition);
        var back = JsonSerializer.Deserialize(json, CatalogJsonContext.Default.GameDefinition)!;

        using var document = JsonDocument.Parse(json);
        Assert.Equal(@"{appData}\Game\Mods", document.RootElement.GetProperty("mods").GetProperty("path").GetString());
        Assert.Equal(game, back);
        Assert.Equal(@"{appData}\Game\Mods", back.Mods!.Path);
    }

    [Fact]
    public void A_game_with_no_mods_folder_writes_no_mods_key_and_reads_back_without_one()
    {
        var json = JsonSerializer.Serialize(ExeGame(), CatalogJsonContext.Default.GameDefinition);

        Assert.DoesNotContain("\"mods\"", json);
        Assert.Null(JsonSerializer.Deserialize(json, CatalogJsonContext.Default.GameDefinition)!.Mods);
    }

    [Fact]
    public void A_mods_folder_adds_one_key_and_leaves_every_other_key_as_an_older_build_wrote_it()
    {
        var plain = ExeGame();
        var withMods = plain with { Mods = new ModsLocation(@"{appData}\Game\Mods") };

        using var before = JsonDocument.Parse(JsonSerializer.Serialize(plain, CatalogJsonContext.Default.GameDefinition));
        using var after = JsonDocument.Parse(JsonSerializer.Serialize(withMods, CatalogJsonContext.Default.GameDefinition));

        var keptByAnOlderBuild = after.RootElement.EnumerateObject().Where(property => property.Name != "mods").Select(property => (property.Name, property.Value.GetRawText()));
        Assert.Equal(before.RootElement.EnumerateObject().Select(property => (property.Name, property.Value.GetRawText())), keptByAnOlderBuild);
        Assert.Equal("saves", after.RootElement.GetProperty("external").GetProperty("role").GetString());
    }

    [Fact]
    public async Task A_games_file_from_the_previous_release_still_loads_unchanged_and_is_saved_without_a_mods_key()
    {
        await File.WriteAllTextAsync(PathOf("games.json"), _v0916Games, Ct);

        var game = Assert.Single(await Store.ListGamesAsync(Ct));

        Assert.Null(game.Mods);
        Assert.Equal(@"C:\Games\SkyrimSE", game.Install.Path);
        Assert.Equal(@"{documents}\My Games\Skyrim Special Edition\Saves", game.External!.Path);
        Assert.Equal(["**/*.log"], game.IgnorePaths);
        Assert.Equal(["**/*.dmp"], game.InstallIgnorePaths);
        await Store.SaveGameAsync(game with { Name = "Skyrim SE" }, Ct);
        Assert.DoesNotContain("\"mods\"", await File.ReadAllTextAsync(PathOf("games.json"), Ct));
    }

    [Fact]
    public async Task A_saved_mods_folder_is_read_back_from_the_games_file()
    {
        var game = ExeGame() with { Mods = new ModsLocation(@"{appData}\Game\Mods") };

        await Store.SaveGameAsync(game, Ct);

        Assert.Equal(game, Assert.Single(await new CatalogStore(Dir).ListGamesAsync(Ct)));
    }

    [Fact]
    public void A_saves_role_of_mods_is_not_read_so_an_older_build_never_meets_it()
    {
        const string json = """{ "id": "g", "install": { "path": "C:\\G" }, "external": { "path": "D:\\M", "role": "mods" } }""";

        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize(json, CatalogJsonContext.Default.GameDefinition));
    }

    [Fact]
    public void Two_definitions_that_differ_only_in_the_mods_folder_are_not_equal()
    {
        var plain = new GameDefinition(Id: "g");
        var withMods = plain with { Mods = new ModsLocation(@"D:\Mods") };

        Assert.NotEqual(plain, withMods);
        Assert.Equal(withMods, plain with { Mods = new ModsLocation(@"D:\Mods") });
        Assert.Equal(withMods.GetHashCode(), (plain with { Mods = new ModsLocation(@"D:\Mods") }).GetHashCode());
        Assert.NotEqual(withMods, plain with { Mods = new ModsLocation(@"D:\Other") });
    }

    [Fact]
    public void Normalizing_trims_the_mods_path_and_keeps_a_game_without_one_as_it_was()
    {
        var trimmed = CatalogValidator.Normalize(ExeGame() with { Mods = new ModsLocation("  D:\\Mods  ") });

        Assert.Equal(@"D:\Mods", trimmed.Mods!.Path);
        Assert.Null(CatalogValidator.Normalize(ExeGame()).Mods);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_mods_folder_with_no_path_is_reported_on_the_mods_field(string path)
    {
        var issues = CatalogValidator.Validate(ExeGame() with { Mods = new ModsLocation(path) });

        Assert.Equal([Issue("mods.path", ValidationMessageIds.GameModsPathRequired)], issues);
    }

    [Fact]
    public void A_mods_folder_beside_the_install_and_the_saves_is_valid()
    {
        Assert.Empty(CatalogValidator.Validate(WithMods(@"C:\Games\Game", @"{appData}\Game\Saves", @"{appData}\Game\Mods")));
    }

    [Theory]
    [MemberData(nameof(NestedFolders))]
    public void A_mods_folder_that_nests_with_another_folder_is_reported_on_the_mods_field(string install, string? saves, string mods, string because)
    {
        var issues = CatalogValidator.Validate(WithMods(install, saves, mods));

        Assert.True(issues.SequenceEqual([Issue("mods.path", ValidationMessageIds.GamePathsNested)]), because);
    }

    [Fact]
    public void A_saves_folder_inside_the_install_is_still_reported_on_the_saves_field_when_there_is_a_mods_folder()
    {
        var issues = CatalogValidator.Validate(WithMods(@"C:\Games\Game", @"C:\Games\Game\Saves", @"D:\Mods"));

        Assert.Equal([Issue("external.path", ValidationMessageIds.GamePathsNested)], issues);
    }

    [Fact]
    public void A_mods_folder_does_not_get_the_install_only_rules_but_the_game_wide_ones()
    {
        var game = ExeGame() with { IgnorePaths = ["**/*.tmp"], InstallIgnorePaths = ["**/*.log"], Mods = new ModsLocation(@"D:\Mods") };

        Assert.Equal(["**/*.tmp"], game.IgnorePathsFor(RepoRole.Mods));
        Assert.Equal(["**/*.log", "**/*.tmp"], game.IgnorePathsFor(RepoRole.Install));
    }

    [Fact]
    public void The_mods_role_is_stored_as_mods()
    {
        var json = JsonSerializer.Serialize(RepoRole.Mods, new JsonSerializerOptions());

        Assert.Equal("\"mods\"", json);
    }
}
