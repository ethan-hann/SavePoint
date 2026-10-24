using Savepoint.Core.Paths;
using Savepoint.Core.Platform;
using Savepoint.Core.Platform.Linux;
using Savepoint.Core.Platform.MacOS;
using Savepoint.Core.Platform.Windows;
using Savepoint.Core.Tests.Paths;

namespace Savepoint.Core.Tests.Platform;

// Smoke tests against the machine running the tests; nothing here writes outside a temp folder.
public class PlatformFactoryTests
{
    private static readonly string _realHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    [Fact]
    public void ForCurrentOs_builds_the_platform_of_the_running_os()
    {
        var platform = PlatformFactory.ForCurrentOs();

        Assert.Equal(PlatformFactory.CurrentOs, platform.Os);
        Assert.Equal(OperatingSystem.IsWindows(), platform.Os == OsKind.Windows);
        Assert.Equal(OperatingSystem.IsMacOS(), platform.Os == OsKind.MacOS);
        Assert.Equal(OperatingSystem.IsLinux(), platform.Os == OsKind.Linux);
    }

    [Fact]
    public void Its_directories_and_tokens_are_built_from_the_real_home_folder()
    {
        var platform = PlatformFactory.ForCurrentOs();
        var syntax = PathSyntax.For(platform.Os);

        Assert.Equal(syntax.Normalize(_realHome), platform.Directories.HomeDir);
        Assert.Equal(syntax.Normalize(_realHome), platform.PathTokens.GetValueOrDefault("userHome"));
        Assert.All(
            new[] { platform.Directories.CatalogDir, platform.Directories.DefaultStorageRoot, platform.Directories.LogsDir, platform.Directories.RuntimeDir },
            dir => Assert.True(syntax.IsAbsolute(dir), dir));
    }

    // The real machine's tables without the Steam lookup, which would read the real registry and Steam folder.
    private static IPlatform CurrentOsWithoutSteam()
    {
        var environment = PlatformEnvironment.FromCurrentMachine() with { GetSteamFolder = static () => null };
        return PlatformFactory.CurrentOs switch
        {
            OsKind.Windows => new WindowsPlatform(environment),
            OsKind.MacOS => new MacOSPlatform(environment),
            _ => new LinuxPlatform(environment),
        };
    }

    [Fact]
    public void Its_denylist_protects_the_real_home_folder_and_the_system_folder()
    {
        var platform = CurrentOsWithoutSteam();
        var syntax = PathSyntax.For(platform.Os);
        var entries = platform.Denylist([]).Entries;

        var home = Assert.Single(entries, e => string.Equals(e.Path, syntax.Normalize(_realHome), syntax.Comparison));
        Assert.Equal(DenylistTier.ExactOnly, home.Tier);
        var system = platform.Os switch
        {
            OsKind.Windows => Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            OsKind.MacOS => "/System",
            _ => "/etc",
        };
        var systemEntry = Assert.Single(entries, e => string.Equals(e.Path, syntax.Normalize(system), syntax.Comparison));
        Assert.Equal(DenylistTier.Subtree, systemEntry.Tier);
    }

    [Fact]
    public void PathSafety_on_the_real_platform_refuses_the_real_home_and_accepts_a_temp_game_folder()
    {
        var platform = CurrentOsWithoutSteam();
        using var tree = new TempTree();
        var safety = new PathSafety(platform, platform.Directories.DefaultStorageRoot);

        PathSafetyAssert.Blocked(safety, _realHome, 2);
        PathSafetyAssert.Accepted(safety, tree.Dir("Games", "Skyrim"));
    }

    [Fact]
    public void The_machine_environment_reports_home_variables_files_and_drives()
    {
        var environment = PlatformEnvironment.FromCurrentMachine();
        using var tree = new TempTree();
        var file = tree.File("user-dirs.dirs", "XDG_DOCUMENTS_DIR=\"$HOME/Docs\"");

        Assert.Equal(_realHome, environment.HomeDirectory);
        Assert.False(string.IsNullOrEmpty(environment.GetVariable("PATH")));
        Assert.Null(environment.GetVariable("SAVEPOINT_SURELY_NOT_SET"));
        Assert.Equal("XDG_DOCUMENTS_DIR=\"$HOME/Docs\"", environment.ReadTextFile(file));
        Assert.Null(environment.ReadTextFile(Path.Combine(tree.Root, "missing.txt")));
        Assert.Equal(OperatingSystem.IsWindows(), environment.ListDriveRoots().Count > 0);
    }

    [Fact]
    public void The_machine_environment_names_this_computer()
    {
        var names = PlatformEnvironment.FromCurrentMachine().LocalHostNames();

        Assert.Contains(Environment.MachineName, names, StringComparer.OrdinalIgnoreCase);
        Assert.All(names, name => Assert.False(string.IsNullOrWhiteSpace(name)));
    }

    [Fact]
    public void Network_paths_that_reach_this_computers_own_folders_are_refused_on_the_real_platform()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Network paths with a host are a Windows form.");
        var platform = PlatformFactory.ForCurrentOs();
        var safety = new PathSafety(platform, platform.Directories.DefaultStorageRoot);

        PathSafetyAssert.Blocked(safety, @"\\localhost\C$\Windows\System32", 1);
        PathSafetyAssert.Blocked(safety, $@"\\{Environment.MachineName}\C$\Users", 1);
        PathSafetyAssert.Blocked(safety, $@"\\{Environment.MachineName.ToLowerInvariant()}\Users\Public", 1);
        PathSafetyAssert.Blocked(safety, @"\\127.0.0.1\ADMIN$\System32", 1);
    }

    [Fact]
    public void Windows_known_folders_resolve_through_the_shell()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Known folders are a Windows shell feature.");
        var environment = PlatformEnvironment.FromCurrentMachine();

        // Downloads, Saved Games, and LocalLow have no Environment.SpecialFolder, so a null here means the shell call is broken.
        foreach (var folder in new[] { KnownFolder.Downloads, KnownFolder.SavedGames, KnownFolder.LocalLow })
        {
            var path = environment.GetKnownFolder(folder);
            Assert.True(path is not null && Path.IsPathRooted(path), $"{folder} resolved to '{path}'");
            Assert.Equal(path, WindowsKnownFolders.Get(folder));
        }

        Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), environment.GetKnownFolder(KnownFolder.Documents));
        Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.Windows), environment.GetKnownFolder(KnownFolder.Windows));
        Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), environment.GetKnownFolder(KnownFolder.LocalAppData));
        Assert.EndsWith(@"AppData\LocalLow", environment.GetKnownFolder(KnownFolder.LocalLow), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Known_folders_are_not_reported_off_windows()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows reports known folders.");

        Assert.Null(PlatformEnvironment.FromCurrentMachine().GetKnownFolder(KnownFolder.Downloads));
    }
}
