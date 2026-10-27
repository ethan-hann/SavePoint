using Savepoint.Core.Api;

namespace Savepoint.Core.Tests.Api;

public class SnapshotFolderNamesTests
{
    [Theory]
    [InlineData("install.git", "install")]
    [InlineData("INSTALL.GIT", "install")]
    [InlineData("mods.git", "mods")]
    [InlineData("Mods.Git", "mods")]
    [InlineData("external.git", "saves")]
    [InlineData("objects", "other")]
    [InlineData("mods", "other")]
    public void A_folder_name_is_one_of_the_three_plain_parts_or_other(string name, string part)
    {
        Assert.Equal(part, SnapshotFolderNames.PartOf(name));
    }

    [Fact]
    public void A_path_inside_a_mods_store_is_described_as_the_game_folder_that_holds_it_and_the_mods_part()
    {
        var gameFolder = Path.Combine("storage", "alpha-1");

        var (path, part) = SnapshotFolderNames.Describe(Path.Combine(gameFolder, "mods.git", "objects", "pack"));

        Assert.Equal(gameFolder, path);
        Assert.Equal(SnapshotFolderNames.Mods, part);
    }

    [Fact]
    public void Leftovers_in_all_three_stores_of_one_game_are_listed_once_and_never_name_an_engine_folder()
    {
        var gameFolder = Path.Combine("storage", "alpha-1");

        var shown = SnapshotFolderNames.Plain(
        [
            Path.Combine(gameFolder, "install.git", "objects"),
            Path.Combine(gameFolder, "mods.git"),
            Path.Combine(gameFolder, "external.git", "refs"),
        ]);

        Assert.Equal([gameFolder], shown);
    }
}
