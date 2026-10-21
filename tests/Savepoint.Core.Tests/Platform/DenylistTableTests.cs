using Savepoint.Core.Paths;
using Savepoint.Core.Platform;
using Savepoint.Core.Platform.Linux;
using Savepoint.Core.Platform.MacOS;
using Savepoint.Core.Platform.Windows;

namespace Savepoint.Core.Tests.Platform;

// The tables are data: these tests pin every entry, so a change to a table is a visible change here.
public class DenylistTableTests
{
    private static readonly string[] _windowsTier1 = [@"C:\Windows"];

    private static readonly string[] _windowsTier2 =
    [
        @"C:\",
        @"D:\",
        @"C:\Program Files",
        @"C:\Program Files (x86)",
        @"C:\ProgramData",
        @"C:\Users",
        @"C:\Users\Test",
        @"C:\Users\Test\AppData",
        @"C:\Users\Test\AppData\Roaming",
        @"C:\Users\Test\AppData\Local",
        @"C:\Users\Test\AppData\LocalLow",
        @"C:\Users\Test\Documents",
        @"C:\Users\Test\OneDrive\Documents",
        @"C:\Users\Test\Desktop",
        @"C:\Users\Test\OneDrive\Desktop",
        @"C:\Users\Test\Downloads",
        @"C:\Users\Test\Saved Games",
        @"C:\Users\Test\OneDrive",
        @"C:\Users\Public",
        @"C:\Users\Test\Pictures",
        @"C:\Users\Test\Music",
        @"C:\Users\Test\Videos",
        @"C:\Program Files (x86)\Steam",
        @"C:\Program Files (x86)\Steam\steamapps",
        @"C:\Program Files (x86)\Steam\steamapps\common",
    ];

    private static readonly string[] _linuxTier1 =
    [
        "/etc", "/boot", "/proc", "/sys", "/dev", "/run", "/usr", "/bin", "/sbin", "/lib", "/lib32", "/lib64", "/libx32",
    ];

    private static readonly string[] _linuxTier2 =
    [
        "/",
        "/home",
        "/home/test",
        "/home/test/.config",
        "/home/test/.local",
        "/home/test/.local/share",
        "/home/test/Documents",
        "/home/test/Desktop",
        "/home/test/Downloads",
        "/mnt",
        "/media",
        "/opt",
        "/srv",
        "/var",
        "/tmp",
        "/home/test/.steam",
        "/home/test/.local/share/Steam",
        "/home/test/.local/share/Steam/steamapps",
        "/home/test/.local/share/Steam/steamapps/common",
        "/root",
        "/home/test/.cache",
        "/home/test/.local/state",
        "/home/test/.var",
        "/home/test/.var/app",
        "/home/test/Pictures",
        "/home/test/Music",
        "/home/test/Videos",
        "/home/test/.local/share/Steam/steamapps/compatdata",
        "/run/media",
        "/run/media/test",
        "/media/test",
    ];

    private static readonly string[] _macTier1 =
    [
        "/System", "/Library", "/bin", "/sbin", "/usr", "/private/etc", "/etc", "/dev", "/cores", "/private/var/db",
    ];

    private static readonly string[] _macTier2 =
    [
        "/",
        "/Users",
        "/Users/test",
        "/Users/test/Library",
        "/Users/test/Library/Application Support",
        "/Users/test/Documents",
        "/Users/test/Desktop",
        "/Users/test/Downloads",
        "/Applications",
        "/Users/test/Applications",
        "/Volumes",
        "/private",
        "/private/var",
        "/private/tmp",
        "/var",
        "/tmp",
        "/opt",
        "/Users/Shared",
        "/Users/test/Pictures",
        "/Users/test/Music",
        "/Users/test/Movies",
        "/Users/test/Library/Containers",
        "/Users/test/Library/Preferences",
        "/Users/test/Library/Caches",
        "/Users/test/Library/Application Support/Steam",
        "/Users/test/Library/Application Support/Steam/steamapps",
        "/Users/test/Library/Application Support/Steam/steamapps/common",
    ];

    [Fact]
    public void Windows_table_matches_the_documented_entries()
    {
        var list = new WindowsPlatform(FakeEnvironment.Windows().Build()).Denylist([@"C:\SavePointData\Catalog", @"D:\SavePointStorage"]);

        AssertTable(list, [.. _windowsTier1, @"C:\SavePointData\Catalog", @"D:\SavePointStorage"], _windowsTier2);
    }

    [Fact]
    public void Linux_table_matches_the_documented_entries()
    {
        var list = new LinuxPlatform(FakeEnvironment.Linux().Build()).Denylist(["/home/test/.config/savepoint", "/data/savepoint"]);

        AssertTable(list, [.. _linuxTier1, "/home/test/.config/savepoint", "/data/savepoint"], _linuxTier2);
    }

    [Fact]
    public void MacOS_table_matches_the_documented_entries()
    {
        var list = new MacOSPlatform(FakeEnvironment.MacOS().Build()).Denylist(["/Users/test/Library/Application Support/SavePoint"]);

        AssertTable(list, [.. _macTier1, "/Users/test/Library/Application Support/SavePoint"], _macTier2);
    }

    [Fact]
    public void Linux_table_adds_xdg_locations_that_differ_from_the_defaults()
    {
        var env = FakeEnvironment.Linux();
        env.Variables["XDG_CONFIG_HOME"] = "/custom/config";
        env.Variables["XDG_DATA_HOME"] = "/custom/data";
        env.Variables["XDG_STATE_HOME"] = "/custom/state";
        env.Files["/custom/config/user-dirs.dirs"] =
            "XDG_DOCUMENTS_DIR=\"/data/docs\"\nXDG_DESKTOP_DIR=\"$HOME/Desk\"\nXDG_DOWNLOAD_DIR=\"/data/dl\"";

        var list = new LinuxPlatform(env.Build()).Denylist([]);

        string[] added =
        [
            "/custom/config",
            "/custom/data",
            "/custom/state",
            "/data/docs",
            "/home/test/Desk",
            "/data/dl",
            "/custom/data/Steam",
            "/custom/data/Steam/steamapps",
            "/custom/data/Steam/steamapps/common",
            "/custom/data/Steam/steamapps/compatdata",
        ];
        AssertTable(list, _linuxTier1, [.. _linuxTier2, .. added]);
    }

    [Fact]
    public void Linux_run_is_blocked_except_for_removable_media_and_nothing_else_carries_an_exception()
    {
        var run = Assert.Single(
            new LinuxPlatform(FakeEnvironment.Linux().Build()).Denylist([]).Entries,
            e => e is { Path: "/run", Tier: DenylistTier.Subtree });
        var others = new[]
        {
            new LinuxPlatform(FakeEnvironment.Linux().Build()).Denylist([]),
            new WindowsPlatform(FakeEnvironment.Windows().Build()).Denylist([]),
            new MacOSPlatform(FakeEnvironment.MacOS().Build()).Denylist([]),
        }.SelectMany(list => list.Entries).Where(e => e.Path != "/run");

        Assert.Equal(["/run/media"], run.AllowedSubRoots);
        Assert.All(others, e => Assert.Null(e.AllowedSubRoots));
    }

    [Fact]
    public void Removable_media_folders_are_listed_for_every_name_the_user_goes_by()
    {
        var env = FakeEnvironment.Linux();
        env.Variables["USER"] = "ethan";
        env.Variables["LOGNAME"] = "ethan-hann";

        var tier2 = new LinuxPlatform(env.Build()).Denylist([]).Entries.Where(e => e.Tier == DenylistTier.ExactOnly).Select(e => e.Path).ToList();

        string[] expected =
        [
            "/run/media/ethan", "/run/media/ethan-hann", "/run/media/test",
            "/media/ethan", "/media/ethan-hann", "/media/test",
        ];
        Assert.All(expected, path => Assert.Contains(path, tier2));
    }

    [Fact]
    public void A_drive_the_lister_reports_gets_its_root_blocked()
    {
        var env = FakeEnvironment.Windows();
        env.DriveRoots.Add(@"E:\");

        var list = new WindowsPlatform(env.Build()).Denylist([]);

        Assert.Contains(list.Entries, e => e is { Path: @"E:\", Tier: DenylistTier.ExactOnly });
    }

    [Fact]
    public void Windows_roots_come_from_the_environment_when_it_differs_from_the_defaults()
    {
        var env = FakeEnvironment.Windows();
        env.Variables["SystemRoot"] = @"D:\WinNT";
        env.Variables["ProgramFiles"] = @"D:\Apps";

        var list = new WindowsPlatform(env.Build()).Denylist([]);

        Assert.Contains(list.Entries, e => e is { Path: @"D:\WinNT", Tier: DenylistTier.Subtree });
        Assert.Contains(list.Entries, e => e is { Path: @"D:\Apps", Tier: DenylistTier.ExactOnly });
    }

    [Fact]
    public void An_extra_root_is_normalized_and_beats_a_tier_two_entry_for_the_same_path()
    {
        var list = new WindowsPlatform(FakeEnvironment.Windows().Build()).Denylist(["c:/program files/", "d:/Store//Data"]);

        var programFiles = Assert.Single(list.Entries, e => string.Equals(e.Path, @"C:\Program Files", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(DenylistTier.Subtree, programFiles.Tier);
        Assert.Contains(list.Entries, e => e is { Path: @"D:\Store\Data", Tier: DenylistTier.Subtree });
    }

    [Fact]
    public void A_relative_extra_root_is_rejected()
    {
        var platform = new LinuxPlatform(FakeEnvironment.Linux().Build());

        Assert.Throws<ArgumentException>(() => platform.Denylist(["storage/root"]));
    }

    [Theory]
    [InlineData(OsKind.Windows)]
    [InlineData(OsKind.Linux)]
    [InlineData(OsKind.MacOS)]
    public void Every_entry_is_absolute_normalized_unique_and_explained(OsKind os)
    {
        var list = os switch
        {
            OsKind.Windows => new WindowsPlatform(FakeEnvironment.Windows().Build()).Denylist([]),
            OsKind.Linux => new LinuxPlatform(FakeEnvironment.Linux().Build()).Denylist([]),
            _ => new MacOSPlatform(FakeEnvironment.MacOS().Build()).Denylist([]),
        };
        var syntax = PathSyntax.For(os);

        Assert.NotEmpty(list.Entries);
        Assert.All(list.Entries, e =>
        {
            Assert.True(syntax.IsAbsolute(e.Path), e.Path);
            Assert.Equal(syntax.Normalize(e.Path), e.Path);
            Assert.False(string.IsNullOrWhiteSpace(e.Reason), e.Path);
        });
        Assert.Equal(list.Entries.Count, list.Entries.Select(e => e.Path).Distinct(StringComparer.FromComparison(syntax.Comparison)).Count());
    }

    private static void AssertTable(PathDenylist list, IEnumerable<string> tier1, IEnumerable<string> tier2)
    {
        var actualTier1 = list.Entries.Where(e => e.Tier == DenylistTier.Subtree).Select(e => e.Path);
        var actualTier2 = list.Entries.Where(e => e.Tier == DenylistTier.ExactOnly).Select(e => e.Path);

        Assert.Equal(tier1.Order(StringComparer.Ordinal), actualTier1.Order(StringComparer.Ordinal));
        Assert.Equal(tier2.Order(StringComparer.Ordinal), actualTier2.Order(StringComparer.Ordinal));
    }
}
