using Savepoint.Core.Paths;

namespace Savepoint.Core.Tests.Paths;

// The real identity read on whichever OS runs the test: every OS tells a folder from another one, whatever name leads to it.
public class HostIdentityTests
{
    [Fact]
    public void A_folder_and_a_link_to_it_have_one_identity_and_nothing_else_shares_it()
    {
        using var tree = new TempTree();
        var folder = tree.Dir("folder");
        var other = tree.Dir("other");
        var link = tree.DirectoryLink("link", folder);
        var file = tree.File("file.txt");

        var identity = PathProbes.Host.ReadIdentity(folder);

        Assert.NotNull(identity);
        Assert.Equal(identity, PathProbes.Host.ReadIdentity(link));
        Assert.Equal(identity, PathProbes.Host.ReadIdentity(folder + Path.DirectorySeparatorChar));
        Assert.NotEqual(identity, PathProbes.Host.ReadIdentity(other));
        Assert.NotEqual(identity, PathProbes.Host.ReadIdentity(file));
    }

    [Fact]
    public void A_path_that_does_not_exist_has_no_identity()
    {
        using var tree = new TempTree();
        var folder = tree.Dir("folder");

        Assert.Null(PathProbes.Host.ReadIdentity(Path.Combine(folder, "missing")));
        Assert.Null(PathProbes.Host.ReadIdentity(Path.Combine(tree.Root, "nowhere", "deeper")));
    }

    [Fact]
    public void A_folder_renamed_away_keeps_its_identity_and_the_new_folder_at_its_path_has_another()
    {
        using var tree = new TempTree();
        var folder = tree.Dir("folder");
        var identity = PathProbes.Host.ReadIdentity(folder);

        Directory.Move(folder, folder + "-moved");
        Directory.CreateDirectory(folder);

        Assert.NotNull(identity);
        Assert.Equal(identity, PathProbes.Host.ReadIdentity(folder + "-moved"));
        Assert.NotEqual(identity, PathProbes.Host.ReadIdentity(folder));
    }
}
