using Savepoint.Core.Paths;
using Savepoint.Core.Tests.Platform;

namespace Savepoint.Core.Tests.Paths;

// Real links, all inside the temp tree: "blocked" is an extra tier-1 root and "home" the fake platform's tier-2 profile folder.
public class PathSafetyHostTests
{
    [Fact]
    public void A_link_that_points_into_a_blocked_tree_is_rejected()
    {
        using var tree = new TempTree();
        var blocked = tree.Dir("blocked");
        var safety = new PathSafety(FakePlatforms.ForHost(tree.Dir("home")), [blocked]);
        var link = tree.DirectoryLink(Path.Combine("work", "into-blocked"), tree.Dir("blocked", "inner"));

        PathSafetyAssert.Blocked(safety, link, 1);
        PathSafetyAssert.Blocked(safety, Path.Combine(link, "game", "data"), 1);
    }

    [Fact]
    public void A_link_to_a_protected_folder_is_rejected_exactly_and_its_subfolders_are_accepted()
    {
        using var tree = new TempTree();
        var home = tree.Dir("home");
        var safety = new PathSafety(FakePlatforms.ForHost(home), [tree.Dir("blocked")]);
        var link = tree.DirectoryLink(Path.Combine("work", "into-home"), home);

        PathSafetyAssert.Blocked(safety, link, 2);
        PathSafetyAssert.Accepted(safety, Path.Combine(link, "Games", "Skyrim"));
    }

    [Fact]
    public void A_link_to_an_ordinary_folder_is_accepted()
    {
        using var tree = new TempTree();
        var safety = new PathSafety(FakePlatforms.ForHost(tree.Dir("home")), [tree.Dir("blocked")]);
        var link = tree.DirectoryLink(Path.Combine("work", "to-games"), tree.Dir("games"));

        PathSafetyAssert.Accepted(safety, Path.Combine(link, "Skyrim"));
    }

    [Fact]
    public void A_dangling_link_is_judged_by_where_it_would_lead()
    {
        using var tree = new TempTree();
        var safety = new PathSafety(FakePlatforms.ForHost(tree.Dir("home")), [tree.Dir("blocked")]);
        var gone = tree.Dir("blocked", "gone");
        var link = tree.DirectoryLink(Path.Combine("work", "dangling"), gone);
        Directory.Delete(gone);

        PathSafetyAssert.Blocked(safety, Path.Combine(link, "game"), 1);
    }

    [Fact]
    public void A_link_to_a_folder_that_contains_the_storage_root_is_rejected()
    {
        using var tree = new TempTree();
        var parent = tree.Dir("parent");
        var safety = new PathSafety(FakePlatforms.ForHost(tree.Dir("home")), [Path.Combine(parent, "storage")]);
        var link = tree.DirectoryLink(Path.Combine("work", "to-parent"), parent);

        PathSafetyAssert.Blocked(safety, link, 1);
        PathSafetyAssert.Accepted(safety, Path.Combine(link, "elsewhere"));
    }

    [Fact]
    public void A_blocked_root_that_is_itself_a_link_blocks_its_real_location_too()
    {
        using var tree = new TempTree();
        var real = tree.Dir("storage-real");
        var rootLink = tree.DirectoryLink("storage-link", real);
        var safety = new PathSafety(FakePlatforms.ForHost(tree.Dir("home")), [rootLink]);

        PathSafetyAssert.Blocked(safety, Path.Combine(real, "game-id", "install.git"), 1);
        PathSafetyAssert.Blocked(safety, Path.Combine(rootLink, "game-id"), 1);
    }

    [Fact]
    public void Overlap_sees_through_links()
    {
        using var tree = new TempTree();
        var safety = new PathSafety(FakePlatforms.ForHost(tree.Dir("home")));
        var real = tree.Dir("real");
        var link = tree.DirectoryLink("link", real);

        Assert.True(safety.PathsOverlap(Path.Combine(link, "sub"), real));
        Assert.True(safety.PathsOverlap(real, link));
        Assert.False(safety.PathsOverlap(link, tree.Dir("other")));
    }

    [Fact]
    public void The_real_path_is_what_the_check_judges()
    {
        using var tree = new TempTree();
        var safety = new PathSafety(FakePlatforms.ForHost(tree.Dir("home")));
        var real = tree.Dir("real", "sub");
        var link = tree.DirectoryLink("link", Path.Combine(tree.Root, "real"));

        Assert.Equal(real, safety.ResolveRealPath(Path.Combine(link, "sub")));
    }
}
