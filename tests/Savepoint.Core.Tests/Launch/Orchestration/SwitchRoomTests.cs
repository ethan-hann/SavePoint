using Savepoint.Core.Catalog;
using Savepoint.Core.Git;
using Savepoint.Core.Launch.Orchestration;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// Folders on one volume are asked for their growth together; folders on other volumes, or on one nobody can name, are not.
public class SwitchRoomTests
{
    private static RepoRef Repo(RepoRole role) => new("git-" + role, "work-" + role, "game-1", role);

    private static FolderGrowth Folder(RepoRole role, string? volume, long growth) => new(Repo(role), volume, growth);

    [Fact]
    public void Three_folders_on_one_volume_are_added_up_under_the_first()
    {
        var shared = SwitchRoom.Shared([Folder(RepoRole.Install, "C:", 10), Folder(RepoRole.Mods, "C:", 20), Folder(RepoRole.External, "C:", 30)]);

        var group = Assert.Single(shared);
        Assert.Equal(Repo(RepoRole.Install), group.Repo);
        Assert.Equal(60, group.GrowthBytes);
    }

    [Fact]
    public void Folders_on_different_volumes_are_not_added_together()
    {
        var shared = SwitchRoom.Shared([Folder(RepoRole.Install, "C:", 10), Folder(RepoRole.External, "D:", 30)]);

        Assert.Empty(shared);
    }

    [Fact]
    public void Only_the_folders_that_share_a_volume_are_added_up()
    {
        var shared = SwitchRoom.Shared([Folder(RepoRole.Install, "C:", 10), Folder(RepoRole.Mods, "D:", 500), Folder(RepoRole.External, "C:", 30)]);

        var group = Assert.Single(shared);
        Assert.Equal(Repo(RepoRole.Install), group.Repo);
        Assert.Equal(40, group.GrowthBytes);
    }

    [Fact]
    public void Two_volumes_that_each_hold_two_folders_are_each_judged()
    {
        var shared = SwitchRoom.Shared([Folder(RepoRole.Install, "C:", 10), Folder(RepoRole.Mods, "D:", 20), Folder(RepoRole.External, "C:", 30), Folder(RepoRole.Install, "D:", 5)]);

        Assert.Equal([40L, 25L], shared.Select(group => group.GrowthBytes));
    }

    [Fact]
    public void A_folder_whose_volume_cannot_be_named_is_left_to_its_own_check()
    {
        var shared = SwitchRoom.Shared([Folder(RepoRole.Install, null, 10), Folder(RepoRole.External, null, 30)]);

        Assert.Empty(shared);
    }

    [Fact]
    public void A_folder_that_shrinks_does_not_make_room_for_another_folders_growth()
    {
        var shared = SwitchRoom.Shared([Folder(RepoRole.Install, "C:", -500), Folder(RepoRole.External, "C:", 30)]);

        Assert.Equal(30, Assert.Single(shared).GrowthBytes);
    }
}
