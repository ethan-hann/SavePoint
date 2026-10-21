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
    ];

    private static readonly string[] _linuxTier1 =
    [
        "/etc", "/boot", "/efi", "/proc", "/sys", "/dev", "/run", "/usr", "/bin", "/sbin", "/lib", "/lib32", "/lib64", "/libx32",
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
        "/root",
        "/home/test/.cache",
        "/home/test/.local/state",
        "/home/test/.var",
        "/home/test/.var/app",
        "/home/test/Pictures",
        "/home/test/Music",
        "/home/test/Videos",
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
        "/Users/test/Library/Mobile Documents",
        "/Users/test/Library/Mobile Documents/com~apple~CloudDocs",
    ];

    private static readonly string[][] _steamNameRules = [["steamapps"], ["steamapps", "common"], ["steamapps", "compatdata"]];

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

        string[] added = ["/custom/config", "/custom/data", "/custom/state", "/data/docs", "/home/test/Desk", "/data/dl", "/custom/data/Steam"];
        AssertTable(list, _linuxTier1, [.. _linuxTier2, .. added]);
    }

    [Fact]
    public void Direct_children_of_the_folders_that_hold_mounted_drives_are_tier_two_on_posix()
    {
        Assert.Equal(
            new[] { "/media", "/media/test", "/mnt", "/run/media/test" },
            ChildFolders(new LinuxPlatform(FakeEnvironment.Linux().Build()).Denylist([])));
        Assert.Equal(["/Volumes"], ChildFolders(new MacOSPlatform(FakeEnvironment.MacOS().Build()).Denylist([])));
        Assert.Empty(ChildFolders(new WindowsPlatform(FakeEnvironment.Windows().Build()).Denylist([])));
    }

    [Theory]
    [InlineData(OsKind.Windows)]
    [InlineData(OsKind.Linux)]
    [InlineData(OsKind.MacOS)]
    public void A_steam_library_is_protected_by_its_folder_names_on_every_os(OsKind os)
    {
        var list = FakePlatforms.For(os).Denylist([]);

        Assert.Equal(
            _steamNameRules.Order(new NameChainComparer()),
            list.NameRules.Select(rule => rule.Names.ToArray()).Order(new NameChainComparer()));
        Assert.All(list.NameRules, rule => Assert.False(string.IsNullOrWhiteSpace(rule.Reason)));
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

        var list = new LinuxPlatform(env.Build()).Denylist([]);

        var tier2 = list.Entries.Where(e => e.Tier == DenylistTier.ExactOnly && !e.ChildrenOnly).Select(e => e.Path).ToList();
        string[] expected =
        [
            "/run/media/ethan", "/run/media/ethan-hann", "/run/media/test",
            "/media/ethan", "/media/ethan-hann", "/media/test",
        ];
        Assert.All(expected, path => Assert.Contains(path, tier2));
        Assert.Equal(
            new[] { "/media", "/media/ethan", "/media/ethan-hann", "/media/test", "/mnt", "/run/media/ethan", "/run/media/ethan-hann", "/run/media/test" },
            ChildFolders(list));
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
    public void Every_source_for_the_windows_folder_is_its_own_entry()
    {
        var env = FakeEnvironment.Windows();
        env.Variables["SystemRoot"] = @"D:\WinNT";
        env.Variables["windir"] = @"E:\Win";
        env.KnownFolders[KnownFolder.Windows] = @"F:\Windows";

        var tier1 = new WindowsPlatform(env.Build()).Denylist([]).Entries.Where(e => e.Tier == DenylistTier.Subtree).Select(e => e.Path);

        Assert.Equal([@"D:\WinNT", @"E:\Win", @"F:\Windows"], tier1.Order());
    }

    [Fact]
    public void The_windows_folder_falls_back_to_windir_and_then_to_the_system_drive()
    {
        var windirOnly = FakeEnvironment.Windows();
        windirOnly.Variables.Remove("SystemRoot");
        windirOnly.Variables["windir"] = @"E:\Win";
        var nothing = FakeEnvironment.Windows();
        nothing.Variables.Remove("SystemRoot");
        nothing.Variables["SystemDrive"] = "D:";

        string[] fromWindir = [.. new WindowsPlatform(windirOnly.Build()).Denylist([]).Entries.Where(e => e.Tier == DenylistTier.Subtree).Select(e => e.Path)];
        string[] fromDrive = [.. new WindowsPlatform(nothing.Build()).Denylist([]).Entries.Where(e => e.Tier == DenylistTier.Subtree).Select(e => e.Path)];

        Assert.Equal([@"E:\Win"], fromWindir);
        Assert.Equal([@"D:\Windows"], fromDrive);
    }

    [Fact]
    public void Every_source_for_program_files_is_its_own_entry_and_the_system_drive_is_the_fallback()
    {
        var env = FakeEnvironment.Windows();
        env.Variables["ProgramFiles"] = @"D:\Apps";
        env.Variables["ProgramW6432"] = @"E:\Apps64";
        env.KnownFolders[KnownFolder.ProgramFiles] = @"F:\Apps";
        env.Variables.Remove("ProgramData");
        env.Variables["SystemDrive"] = "G:";

        var tier2 = new WindowsPlatform(env.Build()).Denylist([]).Entries.Where(e => e.Tier == DenylistTier.ExactOnly).Select(e => e.Path).ToList();

        Assert.Contains(@"D:\Apps", tier2);
        Assert.Contains(@"E:\Apps64", tier2);
        Assert.Contains(@"F:\Apps", tier2);
        Assert.Contains(@"G:\ProgramData", tier2);
    }

    [Fact]
    public void The_onedrive_roots_for_personal_and_work_accounts_are_protected()
    {
        var env = FakeEnvironment.Windows();
        env.Variables["OneDriveConsumer"] = @"C:\Users\Test\OneDrive - Personal";
        env.Variables["OneDriveCommercial"] = @"C:\Users\Test\OneDrive - Work";

        var tier2 = new WindowsPlatform(env.Build()).Denylist([]).Entries.Where(e => e.Tier == DenylistTier.ExactOnly).Select(e => e.Path).ToList();

        Assert.Contains(@"C:\Users\Test\OneDrive - Personal", tier2);
        Assert.Contains(@"C:\Users\Test\OneDrive - Work", tier2);
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
        var list = FakePlatforms.For(os).Denylist([]);
        var syntax = PathSyntax.For(os);

        Assert.NotEmpty(list.Entries);
        Assert.All(list.Entries, e =>
        {
            Assert.True(syntax.IsAbsolute(e.Path), e.Path);
            Assert.Equal(syntax.Normalize(e.Path), e.Path);
            Assert.False(string.IsNullOrWhiteSpace(e.Reason), e.Path);
        });
        Assert.Equal(
            list.Entries.Count,
            list.Entries.Select(e => (e.Path, e.ChildrenOnly)).Distinct(new EntryKeyComparer(syntax.Comparison)).Count());
    }

    private static string[] ChildFolders(PathDenylist list) =>
        [.. list.Entries.Where(e => e.ChildrenOnly).Select(e => e.Path).Order(StringComparer.Ordinal)];

    private static void AssertTable(PathDenylist list, IEnumerable<string> tier1, IEnumerable<string> tier2)
    {
        var actualTier1 = list.Entries.Where(e => e.Tier == DenylistTier.Subtree).Select(e => e.Path);
        var actualTier2 = list.Entries.Where(e => e.Tier == DenylistTier.ExactOnly && !e.ChildrenOnly).Select(e => e.Path);

        Assert.Equal(tier1.Order(StringComparer.Ordinal), actualTier1.Order(StringComparer.Ordinal));
        Assert.Equal(tier2.Order(StringComparer.Ordinal), actualTier2.Order(StringComparer.Ordinal));
    }

    private sealed class NameChainComparer : IComparer<string[]>
    {
        public int Compare(string[]? x, string[]? y) =>
            string.CompareOrdinal(string.Join('/', x ?? []), string.Join('/', y ?? []));
    }

    private sealed class EntryKeyComparer(StringComparison comparison) : IEqualityComparer<(string Path, bool ChildrenOnly)>
    {
        private readonly StringComparer _paths = StringComparer.FromComparison(comparison);

        public bool Equals((string Path, bool ChildrenOnly) x, (string Path, bool ChildrenOnly) y) =>
            x.ChildrenOnly == y.ChildrenOnly && _paths.Equals(x.Path, y.Path);

        public int GetHashCode((string Path, bool ChildrenOnly) obj) => HashCode.Combine(_paths.GetHashCode(obj.Path), obj.ChildrenOnly);
    }
}
