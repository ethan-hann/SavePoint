using System.Text.Json;
using System.Text.RegularExpressions;
using Savepoint.Core.Catalog;

namespace Savepoint.Core.Tests.Catalog;

public class GameTemplatesTests
{
    public static TheoryData<string> TemplateIds => new(GameTemplates.All.Select(t => t.Id));

    [Fact]
    public void Ships_only_the_skyrim_template()
    {
        Assert.Equal(["skyrim-se"], GameTemplates.All.Select(t => t.Id));
    }

    [Fact]
    public void Display_name_is_the_game_name_from_the_definition()
    {
        Assert.Equal(
            ["The Elder Scrolls V: Skyrim Special Edition"],
            GameTemplates.All.Select(t => t.DisplayName));
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

        var draft = template.ToDraft(new FixedRandom());

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
        var template = GameTemplates.All[0];
        var before = template.Definition;

        _ = template.ToDraft();

        Assert.Equal(before, GameTemplates.All[0].Definition);
        Assert.Equal("skyrim-se", GameTemplates.All[0].Definition.Id);
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

    private sealed class FixedRandom : Random
    {
        public override int Next(int maxValue) => 0;
    }
}
