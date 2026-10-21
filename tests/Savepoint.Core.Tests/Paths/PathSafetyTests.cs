using Savepoint.Core.Errors;
using Savepoint.Core.Paths;
using Savepoint.Core.Platform;
using Savepoint.Core.Tests.Platform;

namespace Savepoint.Core.Tests.Paths;

// Lexical checks against each OS's table on any host: the probe sees no links, so the machine's disk never matters.
public class PathSafetyTests
{
    public static TheoryData<OsKind> AllOs => new() { OsKind.Windows, OsKind.Linux, OsKind.MacOS };

    public static TheoryData<OsKind, string> NestedAccepted => new()
    {
        { OsKind.Windows, @"C:\Users\Test\Documents\My Games\Skyrim Special Edition\Saves" },
        { OsKind.Windows, @"C:\Users\Test\OneDrive\Documents\My Games\Skyrim\Saves" },
        { OsKind.Windows, @"C:\Users\Test\Saved Games\SomeGame" },
        { OsKind.Windows, @"C:\Users\Test\AppData\LocalLow\Studio\Game" },
        { OsKind.Windows, @"C:\Users\Test\AppData\Roaming\Game" },
        { OsKind.Windows, @"C:\Program Files (x86)\Steam\steamapps\common\Skyrim" },
        { OsKind.Windows, @"C:\Program Files (x86)\Bethesda\Games\Skyrim Special Edition" },
        { OsKind.Windows, @"D:\Games\Skyrim" },
        { OsKind.Windows, @"\\server\share\Games\Skyrim" },
        { OsKind.Linux, "/home/test/.local/share/Steam/steamapps/common/Game" },
        { OsKind.Linux, "/home/test/Documents/My Games/Game/Saves" },
        { OsKind.Linux, "/home/test/.config/Game" },
        { OsKind.Linux, "/home/test/.local/share/Steam/steamapps/compatdata/489830/pfx/drive_c/users/steamuser/Documents/My Games" },
        { OsKind.Linux, "/mnt/games/Skyrim" },
        { OsKind.Linux, "/opt/games/Game" },
        { OsKind.Linux, "/run/media/test/USB/Games/X" },
        { OsKind.Linux, "/run/media/test/USB" },
        { OsKind.Linux, "/run/media/someone-else/USB/Games/X" },
        { OsKind.Linux, "/media/test/USB/Games/X" },
        { OsKind.MacOS, "/Applications/Game.app" },
        { OsKind.MacOS, "/Users/test/Applications/Game.app" },
        { OsKind.MacOS, "/Users/test/Library/Application Support/Game/Saves" },
        { OsKind.MacOS, "/Users/test/Library/Application Support/Steam/steamapps/common/Game" },
        { OsKind.MacOS, "/Users/test/Documents/Game" },
        { OsKind.MacOS, "/Volumes/External/Games/Game" },
        { OsKind.MacOS, "/private/var/folders/xy/abc123/T/savepoint-test" },
    };

    public static TheoryData<OsKind, string, int> Blocked => new()
    {
        { OsKind.Windows, @"c:\WINDOWS\system32", 1 },
        { OsKind.Windows, "C:/Windows/System32", 1 },
        { OsKind.Windows, @"C:\Games\..\Windows\notepad.exe", 1 },
        { OsKind.Windows, @"C:\Windows\..\Windows", 1 },
        { OsKind.Windows, @"c:\users\TEST", 2 },
        { OsKind.Windows, @"C:\Users\Test\Documents\..", 2 },
        { OsKind.Windows, @"Z:\", 2 },
        { OsKind.Windows, @"\\server\share", 2 },
        { OsKind.Windows, @"\\?\C:\Windows\System32", 1 },
        { OsKind.Windows, @"\\.\C:\Windows", 1 },
        { OsKind.Windows, @"C:\Windows.\System32", 1 },
        { OsKind.Windows, @"C:\Windows\ \..\System32", 1 },
        { OsKind.Windows, @"C:\Windows\...\..\System32", 1 },
        { OsKind.Windows, @"C:\Users\Test. ", 2 },
        { OsKind.Windows, @"C:\Windows::$INDEX_ALLOCATION\System32", 1 },
        { OsKind.Windows, @"C:\Windows::$INDEX_ALLOCATION", 1 },
        { OsKind.Windows, @"C:\Users\Test::$INDEX_ALLOCATION", 1 },
        { OsKind.Windows, @"C:\Windows:$I30:$INDEX_ALLOCATION", 1 },
        { OsKind.Windows, @"C:\Windows\System32:stream", 1 },
        { OsKind.Windows, @"D:\Games\Sky:stream", 1 },
        { OsKind.Windows, @"\\?\C:\Windows::$INDEX_ALLOCATION", 1 },
        { OsKind.Linux, "/home/test/../../etc/passwd", 1 },
        { OsKind.Linux, "/etc/./ssh", 1 },
        { OsKind.Linux, "/home/test/Documents/..", 2 },
        { OsKind.Linux, "/var", 2 },
        { OsKind.Linux, "/run", 1 },
        { OsKind.Linux, "/run/user/1000/app", 1 },
        { OsKind.Linux, "/run/media2/x", 1 },
        { OsKind.Linux, "/run/media", 2 },
        { OsKind.Linux, "/run/media/test", 2 },
        { OsKind.Linux, "/media/test", 2 },
        { OsKind.Linux, "/data", 1 },
        { OsKind.MacOS, "/SYSTEM/Library", 1 },
        { OsKind.MacOS, "/etc/hosts", 1 },
        { OsKind.MacOS, "/applications", 2 },
        { OsKind.MacOS, "/tmp", 2 },
    };

    public static TheoryData<OsKind, string> NotBlocked => new()
    {
        { OsKind.Windows, @"C:\Windows2\x" },
        { OsKind.Windows, @"C:\WindowsApps\x" },
        { OsKind.Windows, @"C:\Users\Test2" },
        { OsKind.Windows, @"C:\WINDOWS \x" },
        { OsKind.Windows, @"C:\Windows..\System32" },
        { OsKind.Windows, @"D:\SavePointStorage2\x" },
        { OsKind.Linux, "/ETC/passwd" },
        { OsKind.Linux, "/etcetera/x" },
        { OsKind.Linux, "/usr2/x" },
        { OsKind.Linux, "/data/savepoint-storage-old/x" },
        { OsKind.MacOS, "/Systems/x" },
        { OsKind.MacOS, "/Library2/x" },
    };

    [Theory]
    [MemberData(nameof(AllOs))]
    public void Every_tier_one_root_and_a_file_beneath_it_is_rejected(OsKind os)
    {
        var (safety, list, syntax) = Build(os);
        var roots = list.Entries.Where(e => e.Tier == DenylistTier.Subtree).ToList();

        Assert.NotEmpty(roots);
        foreach (var root in roots)
        {
            PathSafetyAssert.Blocked(safety, root.Path, 1);
            PathSafetyAssert.Blocked(safety, syntax.Combine(root.Path, "sub/folder/file.txt"), 1);
        }
    }

    [Theory]
    [MemberData(nameof(AllOs))]
    public void Every_tier_two_root_is_rejected_exactly_and_a_folder_beneath_it_is_accepted(OsKind os)
    {
        var (safety, list, syntax) = Build(os);
        var roots = list.Entries.Where(e => e.Tier == DenylistTier.ExactOnly).ToList();

        Assert.NotEmpty(roots);
        foreach (var root in roots)
        {
            PathSafetyAssert.Blocked(safety, root.Path, 2);
            var child = syntax.Combine(root.Path, "SomeGame");
            Assert.True(Record.Exception(() => safety.Check(child)) is null, $"'{child}' should be accepted");
        }
    }

    [Theory]
    [MemberData(nameof(NestedAccepted))]
    public void A_realistic_path_beneath_a_broad_root_is_accepted(OsKind os, string path)
    {
        var (safety, _, _) = Build(os);

        PathSafetyAssert.Accepted(safety, path);
    }

    [Theory]
    [MemberData(nameof(Blocked))]
    public void Case_separators_and_dot_segments_do_not_get_a_path_past_the_table(OsKind os, string path, int tier)
    {
        var (safety, _, _) = Build(os);

        PathSafetyAssert.Blocked(safety, path, tier);
    }

    [Theory]
    [MemberData(nameof(NotBlocked))]
    public void A_sibling_that_only_shares_a_prefix_is_not_blocked(OsKind os, string path)
    {
        var (safety, _, _) = Build(os);

        PathSafetyAssert.Accepted(safety, path);
    }

    [Theory]
    [MemberData(nameof(AllOs))]
    public void SavePoints_own_folders_the_storage_root_and_extra_roots_block_their_descendants(OsKind os)
    {
        var (safety, _, syntax) = Build(os);

        foreach (var root in SafetyFixtures.ProtectedRootsFor(os))
        {
            PathSafetyAssert.Blocked(safety, root, 1);
            PathSafetyAssert.Blocked(safety, syntax.Combine(root, "game-id/install.git/objects"), 1);
        }
    }

    [Theory]
    [MemberData(nameof(AllOs))]
    public void The_catalog_logs_runtime_and_storage_folders_are_protected_even_when_the_caller_adds_nothing(OsKind os)
    {
        var platform = FakePlatforms.For(os);
        var syntax = PathSyntax.For(os);
        var directories = platform.Directories;
        var storage = SafetyFixtures.StorageRootFor(os);

        var safety = new PathSafety(platform, storage, probe: PathProbes.None);

        foreach (var root in new[] { directories.CatalogDir, directories.LogsDir, directories.RuntimeDir, storage })
        {
            PathSafetyAssert.Blocked(safety, root, 1);
            PathSafetyAssert.Blocked(safety, syntax.Combine(root, "inside/file"), 1);
        }
    }

    [Theory]
    [MemberData(nameof(AllOs))]
    public void The_storage_root_passed_in_is_protected_and_the_default_one_is_not_assumed(OsKind os)
    {
        var platform = FakePlatforms.For(os);
        var moved = SafetyFixtures.StorageRootFor(os);

        var safety = new PathSafety(platform, moved, probe: PathProbes.None);

        PathSafetyAssert.Blocked(safety, moved, 1);
        PathSafetyAssert.Accepted(safety, PathSyntax.For(os).Combine(moved + "-other", "Game"));
    }

    [Theory]
    [MemberData(nameof(AllOs))]
    public void Every_ancestor_of_a_tier_one_root_is_rejected(OsKind os)
    {
        var (safety, list, syntax) = Build(os);

        foreach (var root in list.Entries.Where(e => e.Tier == DenylistTier.Subtree))
        {
            var ancestor = syntax.Normalize(syntax.Combine(root.Path, ".."));
            while (!IsVolumeRoot(syntax, ancestor))
            {
                var error = Record.Exception(() => safety.Check(ancestor));
                Assert.True(error is PathSafetyException, $"'{ancestor}' contains the blocked root '{root.Path}' but was accepted");
                ancestor = syntax.Normalize(syntax.Combine(ancestor, ".."));
            }
        }
    }

    [Theory]
    [InlineData(OsKind.Windows, @"C:\Users\Test\AppData\Local\SavePoint\Storage", @"C:\Users\Test\AppData\Local\SavePoint")]
    [InlineData(OsKind.Windows, @"D:\Data\SavePoint\Storage", @"D:\Data\SavePoint")]
    [InlineData(OsKind.Windows, @"D:\Data\SavePoint\Storage", @"D:\Data")]
    [InlineData(OsKind.Linux, "/home/test/.local/share/savepoint/storage", "/home/test/.local/share/savepoint")]
    [InlineData(OsKind.Linux, "/data/savepoint/storage", "/data")]
    [InlineData(OsKind.MacOS, "/Users/test/Library/Application Support/Elsewhere/Storage", "/Users/test/Library/Application Support/Elsewhere")]
    [InlineData(OsKind.MacOS, "/Volumes/Big/SavePoint/Storage", "/Volumes/Big/SavePoint")]
    public void A_folder_that_contains_an_extra_root_is_rejected_as_tier_one(OsKind os, string extraRoot, string ancestor)
    {
        var safety = new PathSafety(FakePlatforms.For(os), SafetyFixtures.StorageRootFor(os), [extraRoot], PathProbes.None);

        PathSafetyAssert.Blocked(safety, extraRoot, 1);
        PathSafetyAssert.Blocked(safety, ancestor, 1);
        PathSafetyAssert.Blocked(safety, ancestor + PathSyntax.For(os).Separator, 1);
        PathSafetyAssert.Accepted(safety, ancestor + "-old");
        PathSafetyAssert.Accepted(safety, PathSyntax.For(os).Combine(ancestor, "other"));
    }

    [Fact]
    public void An_ancestor_that_is_also_a_tier_two_root_keeps_its_tier_two_report()
    {
        var safety = new PathSafety(
            FakePlatforms.For(OsKind.Windows),
            @"C:\Users\Test\AppData\Local\SavePoint\Storage",
            probe: PathProbes.None);

        PathSafetyAssert.Blocked(safety, @"C:\Users\Test\AppData\Local", 2);
        PathSafetyAssert.Blocked(safety, @"C:\Users\Test", 2);
    }

    [Fact]
    public void A_tier_one_rule_can_carry_allowed_sub_roots_without_special_casing_any_path()
    {
        var table = new PathDenylist(
        [
            new DenylistEntry("/srv/system", DenylistTier.Subtree, "test rule", ["/srv/system/ok"]),
            new DenylistEntry("/srv/system/ok/locked", DenylistTier.ExactOnly, "test exact"),
        ]);
        var safety = new PathSafety(new TableOnlyPlatform(table), "/s", probe: PathProbes.None);

        PathSafetyAssert.Accepted(safety, "/srv/system/ok/a/b");
        PathSafetyAssert.Accepted(safety, "/srv/system/ok");
        PathSafetyAssert.Accepted(safety, "/srv/system/ok/locked/inside");
        PathSafetyAssert.Blocked(safety, "/srv/system/ok/locked", 2);
        PathSafetyAssert.Blocked(safety, "/srv/system", 1);
        PathSafetyAssert.Blocked(safety, "/srv/system/other", 1);
        PathSafetyAssert.Blocked(safety, "/srv/system/okay/x", 1);
    }

    [Theory]
    [MemberData(nameof(AllOs))]
    public void A_root_the_caller_did_not_add_is_not_blocked(OsKind os)
    {
        var platform = FakePlatforms.For(os);
        var safety = new PathSafety(platform, SafetyFixtures.StorageRootFor(os), probe: PathProbes.None);

        foreach (var extra in SafetyFixtures.ExtraRootsFor(os))
        {
            PathSafetyAssert.Accepted(safety, PathSyntax.For(os).Combine(extra, "game-id/install.git"));
        }
    }

    [Fact]
    public void The_exception_carries_the_path_as_given_and_the_tier()
    {
        var (safety, _, _) = Build(OsKind.Windows);

        var tier1 = Assert.Throws<PathSafetyException>(() => safety.Check("c:/windows/x"));
        var tier2 = Assert.Throws<PathSafetyException>(() => safety.Check(@"C:\Users"));

        Assert.Equal("error.pathSafety.tier1Blocked", tier1.MessageId);
        Assert.Equal("c:/windows/x", tier1.Args["path"]);
        Assert.Equal(1, tier1.Args["tier"]);
        Assert.Equal("error.pathSafety.tier2Blocked", tier2.MessageId);
        Assert.Equal(2, tier2.Args["tier"]);
    }

    [Fact]
    public void A_relative_path_is_rejected_as_a_programming_error()
    {
        var (safety, _, _) = Build(OsKind.Linux);

        Assert.Throws<ArgumentException>(() => safety.Check("games/skyrim"));
    }

    [Fact]
    public void Roots_are_resolved_once_when_the_check_is_built()
    {
        var syntax = PathSyntax.For(OsKind.Linux);
        var probe = new CountingProbe(new FakePathProbe(syntax));
        var safety = new PathSafety(FakePlatforms.For(OsKind.Linux), SafetyFixtures.StorageRootFor(OsKind.Linux), SafetyFixtures.ExtraRootsFor(OsKind.Linux), probe);
        var callsAfterBuild = probe.Calls;

        safety.Check("/games/skyrim/data/textures");
        var callsForFirstCheck = probe.Calls - callsAfterBuild;
        safety.Check("/games/skyrim/data/textures");
        var callsForSecondCheck = probe.Calls - callsAfterBuild - callsForFirstCheck;

        // Building reads every table root; a check reads only its own path, and the cost does not grow with use.
        Assert.True(callsForFirstCheck > 0, "a check should look at its own path");
        Assert.Equal(callsForFirstCheck, callsForSecondCheck);
        Assert.True(callsAfterBuild > 5 * callsForFirstCheck, $"building read {callsAfterBuild} paths, a check read {callsForFirstCheck}");
    }

    [Theory]
    [InlineData(OsKind.Windows, @"C:\Games\Sky", @"C:\Games\Sky", true)]
    [InlineData(OsKind.Windows, @"C:\Games\Sky", @"C:\Games\Sky\Data", true)]
    [InlineData(OsKind.Windows, @"C:\Games\Sky\Data", @"C:\Games\Sky", true)]
    [InlineData(OsKind.Windows, @"C:\Games\Sky", @"C:\Games\Skyrim", false)]
    [InlineData(OsKind.Windows, @"C:\Games\Sky", @"D:\Games\Sky", false)]
    [InlineData(OsKind.Windows, @"c:\games\SKY", @"C:/Games/Sky/Data", true)]
    [InlineData(OsKind.Windows, @"C:\Games\Sky", @"C:\Games\Other\..\Sky", true)]
    [InlineData(OsKind.Linux, "/games/sky", "/games/sky/data", true)]
    [InlineData(OsKind.Linux, "/games/sky", "/games/skyrim", false)]
    [InlineData(OsKind.Linux, "/Games/sky", "/games/sky/data", false)]
    [InlineData(OsKind.MacOS, "/Games/sky", "/games/SKY/data", true)]
    [InlineData(OsKind.MacOS, "/Games/sky", "/Games/skyrim", false)]
    public void Paths_overlap_when_equal_or_nested_component_wise(OsKind os, string a, string b, bool expected)
    {
        var (safety, _, _) = Build(os);

        Assert.Equal(expected, safety.PathsOverlap(a, b));
        Assert.Equal(expected, safety.PathsOverlap(b, a));
    }

    private static (PathSafety Safety, PathDenylist List, PathSyntax Syntax) Build(OsKind os) => SafetyFixtures.Build(os);

    // A root is the one path whose parent is itself.
    private static bool IsVolumeRoot(PathSyntax syntax, string path) =>
        string.Equals(syntax.Normalize(syntax.Combine(path, "..")), syntax.Normalize(path), syntax.Comparison);

    private sealed class TableOnlyPlatform(PathDenylist table) : IPlatform
    {
        public OsKind Os => OsKind.Linux;

        public StringComparison PathComparison => StringComparison.Ordinal;

        public AppDirectories Directories { get; } = new("/c", "/s", "/l", "/r", "/h");

        public IReadOnlyDictionary<string, string> PathTokens { get; } = new Dictionary<string, string>();

        public IProcessInspector Processes { get; } = new NoProcesses();

        public PathDenylist Denylist(IEnumerable<string> extraBlockedRoots) => table;
    }

    private sealed class NoProcesses : IProcessInspector
    {
        public IReadOnlyList<RunningProcess> List() => [];
    }

    private sealed class CountingProbe(IPathProbe inner) : IPathProbe
    {
        public int Calls { get; private set; }

        public string? ReadLinkTarget(string path)
        {
            Calls++;
            return inner.ReadLinkTarget(path);
        }

        public string? ExpandShortName(string path) => inner.ExpandShortName(path);
    }
}
