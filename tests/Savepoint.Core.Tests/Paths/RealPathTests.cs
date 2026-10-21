using Savepoint.Core.Paths;
using Savepoint.Core.Platform;

namespace Savepoint.Core.Tests.Paths;

public class RealPathTests
{
    private static readonly PathSyntax _linux = PathSyntax.For(OsKind.Linux);
    private static readonly PathSyntax _windows = PathSyntax.For(OsKind.Windows);

    [Fact]
    public void A_path_with_no_links_comes_back_normalized()
    {
        var probe = new FakePathProbe(_linux);

        Assert.Equal("/a/c", RealPath.Resolve("/a/b/../c/", _linux, probe));
    }

    [Fact]
    public void A_link_in_the_middle_is_replaced_by_its_target()
    {
        var probe = new FakePathProbe(_linux).Link("/home/u/games", "/mnt/data/games");

        Assert.Equal("/mnt/data/games/skyrim/saves", RealPath.Resolve("/home/u/games/skyrim/saves", _linux, probe));
    }

    [Fact]
    public void A_relative_target_is_taken_from_the_links_parent_folder()
    {
        var probe = new FakePathProbe(_linux)
            .Link("/a/up", "../b")
            .Link("/a/down", "sub");

        Assert.Equal("/b/x", RealPath.Resolve("/a/up/x", _linux, probe));
        Assert.Equal("/a/sub/x", RealPath.Resolve("/a/down/x", _linux, probe));
    }

    [Fact]
    public void A_chain_of_links_is_followed_to_the_end()
    {
        var probe = new FakePathProbe(_linux)
            .Link("/one", "/two")
            .Link("/two", "/three/deep");

        Assert.Equal("/three/deep/file", RealPath.Resolve("/one/file", _linux, probe));
    }

    [Fact]
    public void Links_inside_a_link_target_are_followed_too()
    {
        var probe = new FakePathProbe(_linux)
            .Link("/a", "/b")
            .Link("/b/c", "/d");

        Assert.Equal("/d/e", RealPath.Resolve("/a/c/e", _linux, probe));
    }

    [Fact]
    public void A_dangling_link_is_replaced_by_its_target_text_and_resolution_continues()
    {
        var probe = new FakePathProbe(_linux).Link("/a/dangling", "/nowhere/x");

        Assert.Equal("/nowhere/x/file", RealPath.Resolve("/a/dangling/file", _linux, probe));
    }

    [Fact]
    public void Dot_dot_after_a_link_climbs_out_of_the_links_target_on_posix()
    {
        var probe = new FakePathProbe(_linux).Link("/a/link", "/x/y");

        Assert.Equal("/x/z", RealPath.Resolve("/a/link/../z", _linux, probe));
    }

    [Fact]
    public void Dot_dot_is_collapsed_before_links_are_read_on_windows_as_win32_does()
    {
        var probe = new FakePathProbe(_windows).Link(@"C:\a\link", @"D:\x\y");

        Assert.Equal(@"C:\a\z", RealPath.Resolve(@"C:\a\link\..\z", _windows, probe));
    }

    [Fact]
    public void A_link_cycle_fails_instead_of_looping()
    {
        var probe = new FakePathProbe(_linux).Link("/a", "/b").Link("/b", "/a");

        Assert.Throws<IOException>(() => RealPath.Resolve("/a/file", _linux, probe));
    }

    [Fact]
    public void A_relative_path_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => RealPath.Resolve("a/b", _linux, new FakePathProbe(_linux)));
    }

    [Fact]
    public void Windows_drive_letter_case_is_normalized()
    {
        Assert.Equal(@"C:\Games", RealPath.Resolve("c:/Games", _windows, new FakePathProbe(_windows)));
    }

    [Fact]
    public void A_windows_junction_to_another_drive_is_followed_and_looked_up_ignoring_case()
    {
        var probe = new FakePathProbe(_windows).Link(@"C:\Games\Link", @"D:\Real");

        Assert.Equal(@"D:\Real\Sky", RealPath.Resolve(@"c:\games\LINK\Sky", _windows, probe));
    }

    [Fact]
    public void A_windows_volume_mount_point_is_left_where_it_is()
    {
        var probe = new FakePathProbe(_windows).Link(@"C:\Mnt\D", @"\??\Volume{1b2c3d}\");

        Assert.Equal(@"C:\Mnt\D\x", RealPath.Resolve(@"C:\Mnt\D\x", _windows, probe));
    }

    [Fact]
    public void A_windows_short_name_is_expanded_to_the_long_name()
    {
        var probe = new FakePathProbe(_windows).ShortName(@"C:\PROGRA~1", "Program Files");

        Assert.Equal(@"C:\Program Files\Game", RealPath.Resolve(@"C:\PROGRA~1\Game", _windows, probe));
    }

    [Fact]
    public void A_windows_unc_path_keeps_its_share_as_the_root()
    {
        Assert.Equal(@"\\srv\share\a\b", RealPath.Resolve(@"\\srv\share\a\.\b", _windows, new FakePathProbe(_windows)));
    }
}
