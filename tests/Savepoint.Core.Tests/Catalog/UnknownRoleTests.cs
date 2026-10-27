using Savepoint.Core.Catalog;
using Savepoint.Core.Paths;
using Savepoint.Core.Services;
using Savepoint.Core.Services.History;
using Savepoint.Core.Tests.Paths;

namespace Savepoint.Core.Tests.Catalog;

// A role that is not one of a game's three folders is a programming error. It must stop the caller, not be taken for the saves folder.
public class UnknownRoleTests
{
    private const RepoRole _notARole = (RepoRole)99;

    [Theory]
    [InlineData(RepoRole.Install, "install")]
    [InlineData(RepoRole.Mods, "mods")]
    [InlineData(RepoRole.External, "saves")]
    public void The_log_names_each_of_the_three_folders(RepoRole role, string name)
    {
        Assert.Equal(name, GameFolders.LogName(role));
    }

    [Fact]
    public void The_log_has_no_name_for_a_role_that_is_not_a_folder()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GameFolders.LogName(_notARole));
    }

    [Fact]
    public void A_consent_marker_cannot_be_written_for_a_role_that_is_not_a_folder()
    {
        using var tree = new TempTree();
        var work = tree.Dir("work");

        Assert.Throws<ArgumentOutOfRangeException>(() => ConsentMarker.Write(work, "game", _notARole, DateTimeOffset.UnixEpoch));
        Assert.False(File.Exists(Path.Combine(work, ConsentMarker.FileName)));
    }

    [Fact]
    public void A_removed_snapshot_has_nothing_to_say_for_a_role_that_is_not_a_folder()
    {
        var group = new RemovedSnapshots.Group("capture", null, null);

        Assert.Throws<ArgumentOutOfRangeException>(() => group.In(_notARole));
    }
}
