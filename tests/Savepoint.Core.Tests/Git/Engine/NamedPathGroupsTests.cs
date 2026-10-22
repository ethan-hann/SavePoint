using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// An index cannot hold a file and a folder of one name, so the paths that decide what the patterns name are split into groups that
// each can.
public class NamedPathGroupsTests
{
    [Fact]
    public void Paths_with_no_file_and_folder_pair_stay_in_one_group()
    {
        var groups = GitEngine.WithoutFileAndFolderPairs(["Mods/A/one.txt", "Mods/A/two.txt", "Mods/B", "game.cfg"]);

        var group = Assert.Single(groups);
        Assert.Equal(4, group.Count);
    }

    [Fact]
    public void A_file_and_a_folder_of_one_name_go_into_different_groups()
    {
        var groups = GitEngine.WithoutFileAndFolderPairs(["Mods/Foo/x.txt", "Mods/Foo", "other.txt"]);

        Assert.Equal(2, groups.Count);
        Assert.DoesNotContain(groups, group => group.Contains("Mods/Foo") && group.Contains("Mods/Foo/x.txt"));
        Assert.Equal(["Mods/Foo", "Mods/Foo/x.txt", "other.txt"], groups.SelectMany(group => group).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Each_level_of_a_nested_pair_needs_a_group_of_its_own()
    {
        var groups = GitEngine.WithoutFileAndFolderPairs(["Foo", "Foo/x", "Foo/x/y"]);

        Assert.Equal(3, groups.Count);
    }

    [Fact]
    public void Names_that_differ_only_in_case_are_a_pair_on_a_file_system_that_ignores_case()
    {
        var groups = GitEngine.WithoutFileAndFolderPairs(["Mods/foo", "Mods/Foo/x.txt"]);

        Assert.Equal(2, groups.Count);
    }
}
