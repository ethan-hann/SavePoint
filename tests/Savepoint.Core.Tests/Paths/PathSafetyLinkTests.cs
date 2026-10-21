using Savepoint.Core.Paths;
using Savepoint.Core.Platform;
using Savepoint.Core.Tests.Platform;

namespace Savepoint.Core.Tests.Paths;

// The real tables against a made-up link layout, so link handling for table roots is checked on any host.
public class PathSafetyLinkTests
{
    [Fact]
    public void A_home_folder_behind_a_link_is_protected_under_both_names()
    {
        var syntax = PathSyntax.For(OsKind.Linux);
        var probe = new FakePathProbe(syntax).Link("/home", "var/home");
        var safety = Safety(OsKind.Linux, probe);

        PathSafetyAssert.Blocked(safety, "/home/test", 2);
        PathSafetyAssert.Blocked(safety, "/var/home/test", 2);
        PathSafetyAssert.Blocked(safety, "/home/test/Documents", 2);
        PathSafetyAssert.Accepted(safety, "/home/test/Documents/My Games/Game");
    }

    [Fact]
    public void A_profile_moved_behind_a_junction_is_protected_under_both_names()
    {
        var syntax = PathSyntax.For(OsKind.Windows);
        var probe = new FakePathProbe(syntax).Link(@"C:\Users\Test", @"D:\Profiles\Test");
        var safety = Safety(OsKind.Windows, probe);

        PathSafetyAssert.Blocked(safety, @"C:\Users\Test", 2);
        PathSafetyAssert.Blocked(safety, @"D:\Profiles\Test", 2);
        PathSafetyAssert.Blocked(safety, @"D:\Profiles\Test\AppData\Local", 2);
        PathSafetyAssert.Accepted(safety, @"C:\Users\Test\Saved Games\Game");
    }

    [Fact]
    public void The_macos_link_layout_is_followed_for_system_folders_and_the_boot_volume_name()
    {
        var syntax = PathSyntax.For(OsKind.MacOS);
        var probe = new FakePathProbe(syntax)
            .Link("/etc", "private/etc")
            .Link("/var", "private/var")
            .Link("/tmp", "private/tmp")
            .Link("/Volumes/Macintosh HD", "/");
        var safety = Safety(OsKind.MacOS, probe);

        PathSafetyAssert.Blocked(safety, "/etc/hosts", 1);
        PathSafetyAssert.Blocked(safety, "/var", 2);
        PathSafetyAssert.Blocked(safety, "/tmp", 2);
        PathSafetyAssert.Blocked(safety, "/Volumes/Macintosh HD", 2);
        PathSafetyAssert.Blocked(safety, "/Volumes/Macintosh HD/System/Library", 1);
        PathSafetyAssert.Accepted(safety, "/Volumes/Macintosh HD/Users/test/Games/Game");
        PathSafetyAssert.Accepted(safety, "/var/folders/xy/abc/T/savepoint-test");
    }

    [Fact]
    public void The_run_media_exception_belongs_to_the_run_entry_and_not_to_every_tier_one_root()
    {
        var os = OsKind.Linux;
        var storage = "/run/media/test/USB/Backups/SavePointStorage";
        var safety = new PathSafety(FakePlatforms.For(os), storage, probe: PathProbes.None);

        PathSafetyAssert.Blocked(safety, storage, 1);
        PathSafetyAssert.Blocked(safety, storage + "/game-id/install.git/objects", 1);
        PathSafetyAssert.Blocked(safety, "/run/media/test/USB/Backups", 1);
        PathSafetyAssert.Accepted(safety, "/run/media/test/USB/Games/X");
        PathSafetyAssert.Accepted(safety, storage + "2/Game");
    }

    [Fact]
    public void A_storage_root_that_is_a_junction_makes_its_containing_folder_off_limits()
    {
        var syntax = PathSyntax.For(OsKind.Windows);
        var probe = new FakePathProbe(syntax).Link(@"D:\Games\SavePoint", @"E:\SavePointData");
        var safety = new PathSafety(FakePlatforms.For(OsKind.Windows), @"D:\Games\SavePoint", probe: probe);

        PathSafetyAssert.Blocked(safety, @"D:\Games", 1);
        PathSafetyAssert.Blocked(safety, @"D:\Games\SavePoint\game-id", 1);
        PathSafetyAssert.Blocked(safety, @"E:\SavePointData", 1);
        PathSafetyAssert.Blocked(safety, @"E:\SavePointData\game-id", 1);
        PathSafetyAssert.Accepted(safety, @"D:\Games\Skyrim");
        PathSafetyAssert.Accepted(safety, @"E:\Other\Game");
    }

    [Fact]
    public void A_storage_root_that_is_a_symlink_makes_its_containing_folder_off_limits_on_linux_too()
    {
        var syntax = PathSyntax.For(OsKind.Linux);
        var probe = new FakePathProbe(syntax).Link("/data/games/savepoint", "/mnt/big/savepoint-data");
        var safety = new PathSafety(FakePlatforms.For(OsKind.Linux), "/data/games/savepoint", probe: probe);

        PathSafetyAssert.Blocked(safety, "/data/games", 1);
        PathSafetyAssert.Blocked(safety, "/mnt/big/savepoint-data/x", 1);
        PathSafetyAssert.Accepted(safety, "/data/games/skyrim");
    }

    [Fact]
    public void A_link_loop_at_a_table_root_does_not_stop_the_guard_from_being_built()
    {
        var syntax = PathSyntax.For(OsKind.Linux);
        var probe = new FakePathProbe(syntax).Link("/home/test/Documents", "/home/test/Documents");

        var built = Record.Exception(() => Safety(OsKind.Linux, probe));

        Assert.Null(built);
        var safety = Safety(OsKind.Linux, probe);
        PathSafetyAssert.Blocked(safety, "/etc/ssh", 1);
        PathSafetyAssert.Blocked(safety, "/home/test", 2);
        Assert.Throws<IOException>(() => safety.Check("/home/test/Documents/Game"));
    }

    private static PathSafety Safety(OsKind os, IPathProbe probe) =>
        new(FakePlatforms.For(os), SafetyFixtures.StorageRootFor(os), SafetyFixtures.ExtraRootsFor(os), probe);
}
