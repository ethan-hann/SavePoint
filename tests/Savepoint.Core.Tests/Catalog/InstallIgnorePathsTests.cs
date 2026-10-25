using System.Text.Json;
using Savepoint.Core.Catalog;

namespace Savepoint.Core.Tests.Catalog;

// Rules for the game's own folder only: stored, compared, cleaned and handed to each folder separately from the game-wide rules.
public class InstallIgnorePathsTests
{
    [Fact]
    public void A_definition_file_without_the_list_reads_as_empty_and_keeps_its_other_rules()
    {
        var game = JsonSerializer.Deserialize("""{ "id": "g", "ignorePaths": ["**/*.tmp"] }""", CatalogJsonContext.Default.GameDefinition)!;

        Assert.Empty(game.InstallIgnorePaths);
        Assert.Equal(["**/*.tmp"], game.IgnorePaths);
    }

    [Fact]
    public void The_list_is_written_and_read_back_under_its_own_name()
    {
        var game = new GameDefinition(Id: "g", InstallIgnorePaths: ["**/*.log"]);

        var json = JsonSerializer.Serialize(game, CatalogJsonContext.Default.GameDefinition);
        var back = JsonSerializer.Deserialize(json, CatalogJsonContext.Default.GameDefinition)!;

        using var document = JsonDocument.Parse(json);
        Assert.Equal("**/*.log", Assert.Single(document.RootElement.GetProperty("installIgnorePaths").EnumerateArray()).GetString());
        Assert.Equal(["**/*.log"], back.InstallIgnorePaths);
        Assert.Empty(back.IgnorePaths);
    }

    [Fact]
    public void Two_definitions_that_differ_only_in_the_install_only_rules_are_not_equal()
    {
        var plain = new GameDefinition(Id: "g");
        var withRule = plain with { InstallIgnorePaths = ["**/*.log"] };

        Assert.NotEqual(plain, withRule);
        Assert.Equal(withRule, plain with { InstallIgnorePaths = ["**/*.log"] });
        Assert.Equal(withRule.GetHashCode(), (plain with { InstallIgnorePaths = ["**/*.log"] }).GetHashCode());
    }

    [Fact]
    public void Normalizing_trims_the_install_only_rules_and_drops_blanks_and_repeats()
    {
        var game = new GameDefinition(Id: "g", InstallIgnorePaths: ["  **/*.log ", "", "**/*.log", "**/*.dmp"]);

        Assert.Equal(["**/*.log", "**/*.dmp"], CatalogValidator.Normalize(game).InstallIgnorePaths);
    }

    [Fact]
    public void The_install_folder_leaves_out_both_lists_and_the_saves_folder_only_the_game_wide_one()
    {
        var game = new GameDefinition(Id: "g", IgnorePaths: ["**/*.tmp"], InstallIgnorePaths: ["**/*.log", "**/*.tmp"]);

        Assert.Equal(["**/*.tmp", "**/*.log"], game.IgnorePathsFor(RepoRole.Install));
        Assert.Equal(["**/*.tmp"], game.IgnorePathsFor(RepoRole.External));
    }
}
