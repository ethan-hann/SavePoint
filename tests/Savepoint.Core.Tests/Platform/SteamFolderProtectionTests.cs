using Savepoint.Core.Paths;
using Savepoint.Core.Platform;
using Savepoint.Core.Platform.Linux;
using Savepoint.Core.Platform.MacOS;
using Savepoint.Core.Platform.Windows;
using Savepoint.Core.Tests.Paths;
using Savepoint.Core.Tests.Services;

namespace Savepoint.Core.Tests.Platform;

// A Steam client or library off the default path must be protected as the default one is: the folder itself is tier 2, and
// a game's own install folder beneath it is fine.
public class SteamFolderProtectionTests
{
    public static TheoryData<OsKind> AllOs => new() { OsKind.Windows, OsKind.Linux, OsKind.MacOS };

    [Theory]
    [MemberData(nameof(AllOs))]
    public void The_steam_folder_the_locator_finds_is_tier_two(OsKind os)
    {
        var machine = Machine.For(os);

        var safety = machine.Safety(machine.Environment());

        PathSafetyAssert.Blocked(safety, machine.SteamFolder, 2);
    }

    [Theory]
    [MemberData(nameof(AllOs))]
    public void Every_library_the_file_lists_is_tier_two_including_an_offline_one(OsKind os)
    {
        var machine = Machine.For(os);

        var safety = machine.Safety(machine.Environment());

        foreach (var library in machine.Libraries)
        {
            PathSafetyAssert.Blocked(safety, library, 2);
        }
    }

    [Theory]
    [MemberData(nameof(AllOs))]
    public void A_games_own_install_folder_under_the_steam_folder_or_a_library_is_accepted(OsKind os)
    {
        var machine = Machine.For(os);
        var syntax = PathSyntax.For(os);

        var safety = machine.Safety(machine.Environment());

        foreach (var root in machine.Libraries.Prepend(machine.SteamFolder))
        {
            PathSafetyAssert.Accepted(safety, syntax.Combine(root, "steamapps/common/Skyrim"));
            PathSafetyAssert.Accepted(safety, syntax.Combine(root, "steamapps/common/Skyrim/Data"));
            PathSafetyAssert.Accepted(safety, syntax.Combine(root, "Backups"));
        }
    }

    [Theory]
    [MemberData(nameof(AllOs))]
    public void The_steamapps_folders_stay_refused_by_name(OsKind os)
    {
        var machine = Machine.For(os);
        var syntax = PathSyntax.For(os);

        var safety = machine.Safety(machine.Environment());

        foreach (var library in machine.Libraries)
        {
            PathSafetyAssert.Blocked(safety, syntax.Combine(library, "steamapps"), 2);
            PathSafetyAssert.Blocked(safety, syntax.Combine(library, "steamapps/common"), 2);
        }
    }

    [Fact]
    public void A_library_spelled_in_another_case_is_still_protected_on_windows()
    {
        var machine = Machine.For(OsKind.Windows);

        var safety = machine.Safety(machine.Environment());

        PathSafetyAssert.Blocked(safety, @"d:\steamlibrary", 2);
        PathSafetyAssert.Blocked(safety, "n:/STEAMLIBRARY/", 2);
    }

    [Theory]
    [MemberData(nameof(AllOs))]
    public void The_folder_and_a_library_that_are_the_same_path_make_one_entry(OsKind os)
    {
        var machine = Machine.For(os);

        var list = machine.Platform(machine.Environment()).Denylist([]);

        Assert.Single(list.Entries, e => PathSyntax.For(os).Normalize(e.Path) == PathSyntax.For(os).Normalize(machine.SteamFolder));
    }

    [Theory]
    [MemberData(nameof(AllOs))]
    public void Only_the_exact_folders_are_added_and_nothing_else_in_the_table_changes(OsKind os)
    {
        var machine = Machine.For(os);
        var baseline = machine.Platform(machine.Environment(withSteam: false)).Denylist([]);

        var list = machine.Platform(machine.Environment()).Denylist([]);

        var added = list.Entries.Where(e => !baseline.Entries.Any(b => b.Path == e.Path && b.Tier == e.Tier && b.ChildrenOnly == e.ChildrenOnly)).ToList();
        Assert.All(added, e => Assert.Equal(DenylistTier.ExactOnly, e.Tier));
        Assert.Equal(
            machine.Libraries.Prepend(machine.SteamFolder).Distinct().Where(path => !baseline.Entries.Any(b => b.Path == path)).Order(StringComparer.Ordinal),
            added.Select(e => e.Path).Order(StringComparer.Ordinal));
        Assert.Equal(baseline.NameRules.Count, list.NameRules.Count);
    }

    [Theory]
    [MemberData(nameof(AllOs))]
    public void A_steam_folder_without_a_library_file_is_still_protected_by_itself(OsKind os)
    {
        var machine = Machine.For(os);
        var env = machine.Environment();
        env.Files.Remove(machine.LibraryFile);

        var safety = machine.Safety(env);

        PathSafetyAssert.Blocked(safety, machine.SteamFolder, 2);
        PathSafetyAssert.Accepted(safety, PathSyntax.For(os).Combine(machine.SteamFolder, "steamapps/common/Skyrim"));
    }

    public static TheoryData<OsKind, string?> BadFiles()
    {
        var data = new TheoryData<OsKind, string?>();
        foreach (var os in new[] { OsKind.Windows, OsKind.Linux, OsKind.MacOS })
        {
            data.Add(os, null);
            data.Add(os, string.Empty);
            data.Add(os, "not a vdf file");
            data.Add(os, "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"/cut");
            data.Add(os, "\0\0\0\0");
            data.Add(os, "oversized");
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(BadFiles))]
    public void A_missing_or_broken_file_leaves_only_the_steam_folder_added(OsKind os, string? text)
    {
        var machine = Machine.For(os);
        var env = machine.Environment();

        // A valid list pushed past any sane size by padding, which a reader with no limit would still accept.
        text = text == "oversized" ? SteamFixtures.FileFor(machine.Libraries) + new string(' ', 3_000_000) : text;
        if (text is null)
        {
            env.Files.Remove(machine.LibraryFile);
        }
        else
        {
            env.Files[machine.LibraryFile] = text;
        }

        var list = machine.Platform(env).Denylist([]);
        var baseline = machine.Platform(machine.Environment(withSteam: false)).Denylist([]);

        var added = list.Entries.Select(e => e.Path).Except(baseline.Entries.Select(e => e.Path)).ToList();
        Assert.Equal(
            new[] { machine.SteamFolder }.Where(path => !baseline.Entries.Any(b => b.Path == path)),
            added);
    }

    [Theory]
    [MemberData(nameof(AllOs))]
    public void A_library_entry_that_is_not_an_absolute_path_is_ignored(OsKind os)
    {
        var machine = Machine.For(os);
        var env = machine.Environment();
        env.Files[machine.LibraryFile] = SteamFixtures.FileFor(machine.SteamFolder, "relative/folder", "..", ".", "~/Steam", "");

        var list = machine.Platform(env).Denylist([]);
        var baseline = machine.Platform(machine.Environment(withSteam: false)).Denylist([]);

        var added = list.Entries.Select(e => e.Path).Except(baseline.Entries.Select(e => e.Path)).ToList();
        Assert.All(added, path => Assert.True(PathSyntax.For(os).IsAbsolute(path), path));
        Assert.Equal(
            new[] { machine.SteamFolder }.Where(path => !baseline.Entries.Any(b => b.Path == path)),
            added);
    }

    [Theory]
    [MemberData(nameof(AllOs))]
    public void A_lookup_that_throws_never_breaks_the_table(OsKind os)
    {
        var machine = Machine.For(os);
        var baseline = machine.Platform(machine.Environment(withSteam: false)).Denylist([]);

        var findFails = machine.Environment().Build() with { GetSteamFolder = () => throw new IOException("registry gone") };
        var readFails = machine.Environment().Build() with { ReadTextFile = path => path == machine.LibraryFile ? throw new UnauthorizedAccessException("locked") : null };

        var withoutFinder = machine.Platform(findFails).Denylist([]);
        var withoutFile = machine.Platform(readFails).Denylist([]);

        Assert.Equal(baseline.Entries.Select(e => e.Path).Order(StringComparer.Ordinal), withoutFinder.Entries.Select(e => e.Path).Order(StringComparer.Ordinal));
        Assert.Contains(withoutFile.Entries, e => e.Path == machine.SteamFolder);
        Assert.DoesNotContain(withoutFile.Entries, e => machine.Libraries.Skip(1).Contains(e.Path));
    }

    [Theory]
    [MemberData(nameof(AllOs))]
    public void The_file_is_read_once_however_often_the_table_is_built(OsKind os)
    {
        var machine = Machine.For(os);
        var env = machine.Environment();
        var platform = machine.Platform(env);

        platform.Denylist([]);
        platform.Denylist([machine.ExtraRoot]);
        var again = platform.Denylist([]);

        Assert.Equal(1, env.Reads[machine.LibraryFile]);
        Assert.Contains(again.Entries, e => e.Path == machine.Libraries[^1]);
    }

    [Theory]
    [MemberData(nameof(AllOs))]
    public void A_library_read_that_fails_keeps_the_folder_protected_and_is_retried_after_a_few_seconds(OsKind os)
    {
        var machine = Machine.For(os);
        var env = machine.Environment();
        env.FailingReads[machine.LibraryFile] = 1;
        var clock = new ManualTimeProvider();
        var platform = machine.Platform(env, clock);

        var first = platform.Denylist([]);
        var sameMoment = platform.Denylist([]);
        clock.Advance(TimeSpan.FromSeconds(6));
        var later = platform.Denylist([]);
        clock.Advance(TimeSpan.FromMinutes(5));
        platform.Denylist([]);

        Assert.Contains(first.Entries, e => e.Path == machine.SteamFolder);
        Assert.DoesNotContain(first.Entries, e => e.Path == machine.Libraries[1]);
        Assert.DoesNotContain(sameMoment.Entries, e => e.Path == machine.Libraries[1]);
        Assert.Contains(later.Entries, e => e.Path == machine.Libraries[1]);
        Assert.Equal(2, env.Reads[machine.LibraryFile]);
    }

    [Theory]
    [MemberData(nameof(AllOs))]
    public void A_library_file_that_appears_later_is_picked_up_on_a_retry(OsKind os)
    {
        var machine = Machine.For(os);
        var env = machine.Environment();
        var text = env.Files[machine.LibraryFile];
        env.Files.Remove(machine.LibraryFile);
        var clock = new ManualTimeProvider();
        var platform = machine.Platform(env, clock);

        var before = platform.Denylist([]);
        env.Files[machine.LibraryFile] = text;
        clock.Advance(TimeSpan.FromSeconds(6));
        var after = platform.Denylist([]);

        Assert.DoesNotContain(before.Entries, e => e.Path == machine.Libraries[1]);
        Assert.Contains(after.Entries, e => e.Path == machine.Libraries[1]);
    }

    [Theory]
    [MemberData(nameof(AllOs))]
    public void A_read_that_keeps_failing_is_tried_at_most_once_per_few_seconds(OsKind os)
    {
        var machine = Machine.For(os);
        var env = machine.Environment();
        env.FailingReads[machine.LibraryFile] = 1000;
        var clock = new ManualTimeProvider();
        var platform = machine.Platform(env, clock);

        for (var i = 0; i < 60; i++)
        {
            platform.Denylist([]);
            clock.Advance(TimeSpan.FromMilliseconds(50));
        }

        Assert.Equal(1, env.Reads[machine.LibraryFile]);
        clock.Advance(TimeSpan.FromSeconds(6));
        platform.Denylist([]);
        Assert.Equal(2, env.Reads[machine.LibraryFile]);
    }

    [Theory]
    [MemberData(nameof(AllOs))]
    public void A_library_that_names_a_tier_one_root_does_not_weaken_it(OsKind os)
    {
        var machine = Machine.For(os);
        var env = machine.Environment();
        var systemRoot = os switch { OsKind.Windows => @"C:\Windows", OsKind.Linux => "/usr", _ => "/System" };
        env.Files[machine.LibraryFile] = SteamFixtures.FileFor(machine.SteamFolder, systemRoot);

        var safety = machine.Safety(env);

        PathSafetyAssert.Blocked(safety, systemRoot, 1);
        PathSafetyAssert.Blocked(safety, PathSyntax.For(os).Combine(systemRoot, "Games/Skyrim"), 1);
    }

    [Fact]
    public void The_real_windows_file_shape_protects_all_four_libraries_with_the_steam_folder_in_the_default_place()
    {
        var env = FakeEnvironment.Windows();
        env.SteamFolder = @"C:\Program Files (x86)\Steam";
        env.Files[@"C:\Program Files (x86)\Steam\steamapps\libraryfolders.vdf"] = SteamFixtures.WindowsFile;
        var platform = new WindowsPlatform(env.Build());
        var safety = new PathSafety(platform, SafetyFixtures.StorageRootFor(OsKind.Windows), SafetyFixtures.ExtraRootsFor(OsKind.Windows), PathProbes.None);

        foreach (var library in new[] { @"C:\Program Files (x86)\Steam", @"D:\SteamLibrary", @"X:\SteamLibrary", @"N:\SteamLibrary" })
        {
            PathSafetyAssert.Blocked(safety, library, 2);
            PathSafetyAssert.Accepted(safety, library + @"\steamapps\common\Skyrim");
        }
    }

    // One fake computer per OS, with Steam off the default place and a library on another drive.
    private sealed record Machine(OsKind Os, string SteamFolder, string[] Libraries, string ExtraRoot)
    {
        public string LibraryFile => PathSyntax.For(Os).Combine(SteamFolder, "steamapps/libraryfolders.vdf");

        public static Machine For(OsKind os) => os switch
        {
            OsKind.Windows => new(os, @"E:\Games\Steam", [@"E:\Games\Steam", @"D:\SteamLibrary", @"N:\SteamLibrary"], @"E:\Extra\SavePoint"),
            OsKind.Linux => new(
                os,
                "/home/test/.var/app/com.valvesoftware.Steam/.local/share/Steam",
                ["/home/test/.var/app/com.valvesoftware.Steam/.local/share/Steam", "/mnt/games/SteamLibrary", "/mnt/offline/SteamLibrary"],
                "/srv/extra-savepoint"),
            _ => new(os, "/Volumes/Games/Steam", ["/Volumes/Games/Steam", "/Volumes/Games/SteamLibrary", "/Volumes/Offline/SteamLibrary"], "/Users/test/Extra/SavePoint"),
        };

        public FakeEnvironment Environment(bool withSteam = true)
        {
            var env = Os switch
            {
                OsKind.Windows => FakeEnvironment.Windows(),
                OsKind.Linux => FakeEnvironment.Linux(),
                _ => FakeEnvironment.MacOS(),
            };

            if (withSteam)
            {
                env.SteamFolder = SteamFolder;
                env.Files[LibraryFile] = SteamFixtures.FileFor(Libraries);
            }

            return env;
        }

        public IPlatform Platform(FakeEnvironment env) => Platform(env.Build());

        public IPlatform Platform(FakeEnvironment env, TimeProvider clock) => Platform(env.Build(), clock);

        public IPlatform Platform(PlatformEnvironment env, TimeProvider? clock = null) => Os switch
        {
            OsKind.Windows => new WindowsPlatform(env, clock: clock),
            OsKind.Linux => new LinuxPlatform(env, clock: clock),
            _ => new MacOSPlatform(env, clock: clock),
        };

        public PathSafety Safety(FakeEnvironment env) =>
            new(Platform(env), SafetyFixtures.StorageRootFor(Os), SafetyFixtures.ExtraRootsFor(Os), PathProbes.None);
    }
}
