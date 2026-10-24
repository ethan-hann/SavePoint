using System.Text.Json;
using System.Text.RegularExpressions;
using Savepoint.Core.Catalog;
using Savepoint.Core.Platform;

namespace Savepoint.Core.Tests.Catalog;

public class GameTemplatesTests
{
    public static TheoryData<string> TemplateIds => new(GameTemplates.All.Select(t => t.Id));

    [Fact]
    public void Ships_the_skyrim_template_and_many_more()
    {
        Assert.Contains("skyrim-se", GameTemplates.All.Select(t => t.Id));
        Assert.True(GameTemplates.All.Count >= 300);
    }

    [Fact]
    public void Templates_come_in_a_fixed_order()
    {
        var ids = GameTemplates.All.Select(t => t.Id).ToList();

        Assert.Equal(ids.Order(StringComparer.Ordinal), ids);
    }

    [Theory]
    [MemberData(nameof(TemplateIds))]
    public void Display_name_is_the_game_name_from_the_definition(string id)
    {
        var template = GameTemplates.All.Single(t => t.Id == id);

        Assert.Equal(template.Definition.Name, template.DisplayName);
        Assert.False(string.IsNullOrWhiteSpace(template.DisplayName));
    }

    [Fact]
    public void Skyrim_display_name_is_the_full_game_name()
    {
        Assert.Equal(
            "The Elder Scrolls V: Skyrim Special Edition",
            GameTemplates.All.Single(t => t.Id == "skyrim-se").DisplayName);
    }

    [Theory]
    [MemberData(nameof(TemplateIds))]
    public void Templates_leave_the_install_folder_for_the_player_to_pick(string id)
    {
        // A drive-letter path would be wrong on Linux and macOS, and no token names a game's install folder.
        Assert.Empty(GameTemplates.All.Single(t => t.Id == id).Definition.Install.Path);
    }

    [Theory]
    [MemberData(nameof(TemplateIds))]
    public void Templates_name_the_saves_folder_with_a_path_token(string id)
    {
        var external = GameTemplates.All.Single(t => t.Id == id).Definition.External;

        Assert.True(external is null || Regex.IsMatch(external.Path, @"^\{[A-Za-z]+\}"));
    }

    [Fact]
    public void Skyrim_template_matches_the_v1_definition_exactly()
    {
        var expected = new GameDefinition
        {
            SchemaVersion = 1,
            Id = "skyrim-se",
            Name = "The Elder Scrolls V: Skyrim Special Edition",
            SteamAppId = 489830,
            Install = new InstallLocation(),
            External = new ExternalLocation(@"{documents}\My Games\Skyrim Special Edition\Saves", ExternalRole.Saves),
            Launch = new LaunchConfig(
                LaunchMethod.Exe,
                ExePath: "SkyrimSELauncher.exe",
                Args: [],
                ProcessNameForExitDetection: "SkyrimSE.exe"),
            IgnorePaths = ["**/*.log", "SKSE/Plugins/*.log", "**/Crash Logs/**"],
            ModPathHints = ["Data", "plugins.txt"],
            Notes = "Direct-exe launch preferred; steam://run/489830 works but risks Steam re-updating the install at launch.",
        };

        Assert.Equal(expected, GameTemplates.All.Single(t => t.Id == "skyrim-se").Definition);
    }

    [Theory]
    [MemberData(nameof(TemplateIds))]
    public void Every_template_is_valid_once_the_player_picks_the_install_folder(string id)
    {
        var template = GameTemplates.All.Single(t => t.Id == id);

        Assert.Equal(["install.path"], CatalogValidator.Validate(template.Definition).Select(issue => issue.Path));
        var picked = template.Definition with { Install = new InstallLocation(@"D:\Picked") };
        Assert.Empty(CatalogValidator.Validate(picked));
        Assert.Equal(picked, CatalogValidator.Normalize(picked));
        Assert.Equal(template.Id, template.Definition.Id);
    }

    [Fact]
    public void Template_list_is_built_once()
    {
        Assert.Same(GameTemplates.All, GameTemplates.All);
    }

    [Fact]
    public void Draft_from_a_template_gets_a_fresh_id_and_keeps_everything_else()
    {
        var template = GameTemplates.All.Single(t => t.Id == "skyrim-se");

        var draft = template.ToDraft(OsKind.Windows, new FixedRandom());

        Assert.Equal("the-elder-scrolls-v-skyrim-special-editi-aaaa", draft.Id);
        Assert.NotEqual(template.Id, draft.Id);
        Assert.Equal(template.Definition with { Id = draft.Id }, draft);
        Assert.Equal(["install.path"], CatalogValidator.Validate(draft).Select(issue => issue.Path));
    }

    [Fact]
    public void Template_lists_are_read_only_so_a_draft_cannot_change_the_shared_template()
    {
        var definition = GameTemplates.All.Single(t => t.Id == "skyrim-se").ToDraft();

        Assert.True(((ICollection<string>)definition.IgnorePaths).IsReadOnly);
        Assert.True(((ICollection<string>)definition.ModPathHints).IsReadOnly);
        Assert.True(((ICollection<string>)definition.Launch.Args).IsReadOnly);
    }

    [Fact]
    public void Drafting_leaves_the_template_itself_unchanged()
    {
        var template = GameTemplates.All.Single(t => t.Id == "skyrim-se");
        var before = template.Definition;

        _ = template.ToDraft();

        Assert.Equal(before, GameTemplates.All.Single(t => t.Id == "skyrim-se").Definition);
        Assert.Equal("skyrim-se", template.Definition.Id);
    }

    [Fact]
    public void Fake_game_fixture_template_is_a_valid_definition()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Catalog", "Fixtures", "fake-game.json");

        var definition = JsonSerializer.Deserialize(File.ReadAllText(path), CatalogJsonContext.Default.GameDefinition)!;

        Assert.Equal("fake-game", definition.Id);
        Assert.Equal(["--saves", "../FakeGameSaves", "--save-every", "1000"], definition.Launch.Args);
        Assert.Equal("FakeGame", definition.Launch.ProcessNameForExitDetection);
        Assert.Equal("{userHome}/SavePointTest/FakeGameSaves", definition.External!.Path);
        Assert.Empty(CatalogValidator.Validate(definition));
    }

    [Fact]
    public void Fake_game_is_not_offered_to_users()
    {
        Assert.DoesNotContain(GameTemplates.All, t => t.Id == "fake-game");
    }

    private static GameTemplate WithSaves(string path) => new(
        "x",
        "X",
        new GameDefinition(Id: "x", Name: "X", External: new ExternalLocation(path, ExternalRole.Saves)));

    [Theory]
    [InlineData(OsKind.Linux)]
    [InlineData(OsKind.MacOS)]
    public void Draft_leaves_the_saves_folder_blank_without_an_entry_for_that_system(OsKind os)
    {
        // Even a token both systems define says nothing about where the game saves there (a Windows game under Proton keeps saves in its prefix).
        Assert.Null(WithSaves(@"{documents}\My Games\Game").ToDraft(os, new FixedRandom()).External);
        Assert.Null(WithSaves(@"{appData}\Game").ToDraft(os, new FixedRandom()).External);
        Assert.Null(WithSaves(@"{localLow}\Studio\Game").ToDraft(os, new FixedRandom()).External);
        Assert.Null(WithSaves(@"C:\Users\Someone\Saves").ToDraft(os, new FixedRandom()).External);
    }

    [Fact]
    public void Draft_on_windows_keeps_the_default_saves_folder()
    {
        var draft = WithSaves(@"{documents}\My Games\Game").ToDraft(OsKind.Windows, new FixedRandom());

        Assert.Equal(@"{documents}\My Games\Game", draft.External!.Path);
    }

    [Fact]
    public void Draft_on_windows_keeps_the_windows_only_tokens()
    {
        Assert.Equal(@"{localLow}\Studio\Game", WithSaves(@"{localLow}\Studio\Game").ToDraft(OsKind.Windows, new FixedRandom()).External!.Path);
    }

    [Fact]
    public void A_per_system_saves_folder_still_wins_over_a_blank_default()
    {
        var hints = new TemplateLibraryHints(SavesByOs: new Dictionary<string, ExternalLocation> { ["linux"] = new("{userHome}/.game/saves", ExternalRole.Saves) });
        var template = WithSaves(@"{localLow}\Studio\Game") with { Hints = hints };

        Assert.Equal("{userHome}/.game/saves", template.ToDraft(OsKind.Linux, new FixedRandom()).External!.Path);
    }

    private sealed class FixedRandom : Random
    {
        public override int Next(int maxValue) => 0;
    }
}
