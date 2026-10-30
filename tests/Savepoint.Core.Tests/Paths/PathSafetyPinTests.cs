using Savepoint.Core.Paths;
using Savepoint.Core.Tests.Platform;

namespace Savepoint.Core.Tests.Paths;

// The real links, but no identities, as on Linux and macOS.
internal sealed class WithoutIdentities : IPathProbe
{
    public string? ReadLinkTarget(string path) => PathProbes.Host.ReadLinkTarget(path);

    public string? ExpandShortName(string path) => PathProbes.Host.ExpandShortName(path);

    public FileIdentity? ReadIdentity(string path) => null;

    public IReadOnlyList<string> ReadFinalPaths(string path) => PathProbes.Host.ReadFinalPaths(path);
}

// A pin says which folder a path led to when it was approved, so a step that cannot be undone can tell whether it is still that folder.
public class PathSafetyPinTests
{
    private static PathSafety Safety(TempTree tree) =>
        new(FakePlatforms.ForHost(tree.Dir("home")), Path.Combine(tree.Root, "own-storage"));

    // A FAT or exFAT folder on Linux: the inode it reports differs from one read to the next.
    private sealed class ChangingIdentities : IPathProbe
    {
        private ulong _reads;

        public string? ReadLinkTarget(string path) => PathProbes.Host.ReadLinkTarget(path);

        public string? ExpandShortName(string path) => PathProbes.Host.ExpandShortName(path);

        public FileIdentity? ReadIdentity(string path) => new FileIdentity(1, ++_reads);

        public bool KeepsStableIdentity(string path) => false;

        public IReadOnlyList<string> ReadFinalPaths(string path) => PathProbes.Host.ReadFinalPaths(path);
    }

    [Fact]
    public void A_folder_on_a_file_system_that_changes_identities_is_pinned_by_its_path_and_stays_pinned()
    {
        using var tree = new TempTree();
        var safety = new PathSafety(FakePlatforms.ForHost(tree.Dir("home")), Path.Combine(tree.Root, "own-storage"), null, new ChangingIdentities());
        var game = tree.Dir("games", "alpha");

        var pin = safety.Pin(game);

        Assert.Null(pin.Identity);
        Assert.True(safety.IsStillPinned(game, pin));
    }

    [Fact]
    public void Where_no_identities_are_reported_a_link_pointed_at_another_folder_is_still_caught_by_the_real_path()
    {
        using var tree = new TempTree();
        var safety = new PathSafety(FakePlatforms.ForHost(tree.Dir("home")), Path.Combine(tree.Root, "own-storage"), null, new WithoutIdentities());
        var link = tree.DirectoryLink("linked", tree.Dir("games", "alpha"));
        var pin = safety.Pin(link);

        Assert.Null(pin.Identity);
        Assert.True(safety.IsStillPinned(link, pin));
        Directory.Delete(link);
        tree.DirectoryLink("linked", tree.Dir("elsewhere"));

        Assert.False(safety.IsStillPinned(link, pin));
    }

    [Fact]
    public void A_pin_holds_the_real_path_and_a_folder_that_has_not_changed_is_still_pinned()
    {
        using var tree = new TempTree();
        var safety = Safety(tree);
        var game = tree.Dir("games", "alpha");

        var pin = safety.Pin(game);

        Assert.Equal(game, pin.RealPath);
        Assert.True(safety.IsStillPinned(game, pin));
        Assert.True(safety.IsStillPinned(game + Path.DirectorySeparatorChar, pin));
    }

    [Fact]
    public void A_pin_of_a_path_reached_through_a_link_holds_the_folder_the_link_leads_to()
    {
        using var tree = new TempTree();
        var safety = Safety(tree);
        var real = tree.Dir("games", "alpha");
        var link = tree.DirectoryLink("linked", real);

        var pin = safety.Pin(link);

        Assert.Equal(real, pin.RealPath);
        Assert.True(safety.IsStillPinned(link, pin));
    }

    [Fact]
    public void A_folder_renamed_away_and_replaced_by_a_link_is_no_longer_pinned()
    {
        using var tree = new TempTree();
        var safety = Safety(tree);
        var game = tree.Dir("games", "alpha");
        var elsewhere = tree.Dir("elsewhere");
        var pin = safety.Pin(game);

        Directory.Move(game, game + "-moved");
        tree.DirectoryLink(Path.Combine("games", "alpha"), elsewhere);

        Assert.False(safety.IsStillPinned(game, pin));
    }

    [Fact]
    public void A_link_pointed_at_another_folder_is_no_longer_pinned()
    {
        using var tree = new TempTree();
        var safety = Safety(tree);
        var link = tree.DirectoryLink("linked", tree.Dir("games", "alpha"));
        var pin = safety.Pin(link);

        Directory.Delete(link);
        tree.DirectoryLink("linked", tree.Dir("elsewhere"));

        Assert.False(safety.IsStillPinned(link, pin));
    }

    [Fact]
    public void A_different_folder_at_the_same_path_is_no_longer_pinned()
    {
        using var tree = new TempTree();
        var safety = Safety(tree);
        var game = tree.Dir("games", "alpha");
        var pin = safety.Pin(game);

        Assert.NotNull(pin.Identity);
        Directory.Move(game, game + "-moved");
        Directory.CreateDirectory(game);

        Assert.False(safety.IsStillPinned(game, pin));
    }
}
