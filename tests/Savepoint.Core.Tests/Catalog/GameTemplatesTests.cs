using System.Text.Json;
using Savepoint.Core.Catalog;

namespace Savepoint.Core.Tests.Catalog;

public class GameTemplatesTests
{
    public static TheoryData<string> TemplateIds => new(GameTemplates.All.Select(t => t.Id));

    [Fact]
    public void Ships_the_skyrim_and_simple_indie_templates()
    {
        Assert.Equal(["skyrim-se", "simple-indie-example"], GameTemplates.All.Select(t => t.Id));
    }

    [Fact]
    public void Display_name_is_the_game_name_from_the_definition()
    {
        Assert.Equal(
            ["The Elder Scrolls V: Skyrim Special Edition", "A Simple Indie Platformer"],
            GameTemplates.All.Select(t => t.DisplayName));
    }

    [Fact]
    public void Skyrim_template_matches_the_v1_definition_exactly()
    {
        var expected = new GameDefinition
        {
            SchemaVersion = 1,
            Id = "skyrim-se",
            Name = "The Elder Scrolls V: Skyrim Special Edition",
            Install = new InstallLocation(@"C:\Games\SkyrimSE"),
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

    [Fact]
    public void Simple_indie_template_matches_the_v1_definition_exactly()
    {
        var expected = new GameDefinition
        {
            SchemaVersion = 1,
            Id = "simple-indie-example",
            Name = "A Simple Indie Platformer",
            Install = new InstallLocation(@"C:\Games\SimpleIndie"),
            External = new ExternalLocation(@"{appData}\SimpleIndie\saves", ExternalRole.Saves),
            Launch = new LaunchConfig(
                LaunchMethod.Exe,
                ExePath: "SimpleIndie.exe",
                Args: [],
                ProcessNameForExitDetection: "SimpleIndie.exe"),
            IgnorePaths = [],
            ModPathHints = ["mods"],
        };

        Assert.Equal(expected, GameTemplates.All.Single(t => t.Id == "simple-indie-example").Definition);
    }

    [Theory]
    [MemberData(nameof(TemplateIds))]
    public void Every_template_passes_validation_unchanged(string id)
    {
        var template = GameTemplates.All.Single(t => t.Id == id);

        Assert.Empty(CatalogValidator.Validate(template.Definition));
        Assert.Equal(template.Definition, CatalogValidator.Normalize(template.Definition));
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
        Assert.Empty(CatalogValidator.Validate(draft));
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
        Assert.Equal(["--title=Fake Game (SavePoint test)"], definition.Launch.Args);
        Assert.Equal("FakeGame.exe", definition.Launch.ProcessNameForExitDetection);
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
