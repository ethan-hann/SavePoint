using Savepoint.Core.Platform;
using Savepoint.Core.Platform.Linux;
using Savepoint.Core.Platform.MacOS;
using Savepoint.Core.Platform.Windows;

namespace Savepoint.Core.Tests.Platform;

public class AppDirectoriesTests
{
    [Fact]
    public void Windows_keeps_the_catalog_in_roaming_and_the_rest_in_local_app_data()
    {
        var platform = new WindowsPlatform(FakeEnvironment.Windows().Build());

        Assert.Equal(
            new AppDirectories(
                @"C:\Users\Test\AppData\Roaming\SavePoint",
                @"C:\Users\Test\AppData\Local\SavePoint\Storage",
                @"C:\Users\Test\AppData\Local\SavePoint\Logs",
                @"C:\Users\Test\AppData\Local\SavePoint\Runtime",
                @"C:\Users\Test"),
            platform.Directories);
    }

    [Fact]
    public void Windows_falls_back_to_the_known_folder_then_to_the_profile_when_a_variable_is_missing()
    {
        var env = FakeEnvironment.Windows();
        env.Variables.Remove("APPDATA");
        env.Variables.Remove("LOCALAPPDATA");
        env.KnownFolders[KnownFolder.RoamingAppData] = @"D:\Redirected\Roaming";

        var directories = new WindowsPlatform(env.Build()).Directories;

        Assert.Equal(@"D:\Redirected\Roaming\SavePoint", directories.CatalogDir);
        Assert.Equal(@"C:\Users\Test\AppData\Local\SavePoint\Storage", directories.DefaultStorageRoot);
    }

    [Fact]
    public void Linux_uses_the_xdg_defaults_under_the_home_folder()
    {
        var platform = new LinuxPlatform(FakeEnvironment.Linux().Build());

        Assert.Equal(
            new AppDirectories(
                "/home/test/.config/savepoint",
                "/home/test/.local/share/savepoint/storage",
                "/home/test/.local/state/savepoint/logs",
                "/home/test/.local/state/savepoint/runtime",
                "/home/test"),
            platform.Directories);
    }

    [Fact]
    public void Linux_honors_the_xdg_variables()
    {
        var env = FakeEnvironment.Linux();
        env.Variables["XDG_CONFIG_HOME"] = "/custom/config";
        env.Variables["XDG_DATA_HOME"] = "/custom/data";
        env.Variables["XDG_STATE_HOME"] = "/custom/state";

        var directories = new LinuxPlatform(env.Build()).Directories;

        Assert.Equal("/custom/config/savepoint", directories.CatalogDir);
        Assert.Equal("/custom/data/savepoint/storage", directories.DefaultStorageRoot);
        Assert.Equal("/custom/state/savepoint/logs", directories.LogsDir);
        Assert.Equal("/custom/state/savepoint/runtime", directories.RuntimeDir);
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative/dir")]
    public void Linux_ignores_an_empty_or_relative_xdg_variable(string value)
    {
        var env = FakeEnvironment.Linux();
        env.Variables["XDG_CONFIG_HOME"] = value;

        Assert.Equal("/home/test/.config/savepoint", new LinuxPlatform(env.Build()).Directories.CatalogDir);
    }

    [Fact]
    public void MacOS_uses_library_folders()
    {
        var platform = new MacOSPlatform(FakeEnvironment.MacOS().Build());

        Assert.Equal(
            new AppDirectories(
                "/Users/test/Library/Application Support/SavePoint",
                "/Users/test/Library/Application Support/SavePoint/Storage",
                "/Users/test/Library/Logs/SavePoint",
                "/Users/test/Library/Caches/SavePoint",
                "/Users/test"),
            platform.Directories);
    }

    [Fact]
    public void Each_platform_reports_its_os_and_path_comparison()
    {
        IPlatform windows = new WindowsPlatform(FakeEnvironment.Windows().Build());
        IPlatform linux = new LinuxPlatform(FakeEnvironment.Linux().Build());
        IPlatform mac = new MacOSPlatform(FakeEnvironment.MacOS().Build());

        Assert.Equal((OsKind.Windows, StringComparison.OrdinalIgnoreCase), (windows.Os, windows.PathComparison));
        Assert.Equal((OsKind.Linux, StringComparison.Ordinal), (linux.Os, linux.PathComparison));
        Assert.Equal((OsKind.MacOS, StringComparison.OrdinalIgnoreCase), (mac.Os, mac.PathComparison));
    }

    [Fact]
    public void A_relative_home_directory_is_rejected()
    {
        var env = new PlatformEnvironment { HomeDirectory = "home/test" };

        Assert.Throws<ArgumentException>(() => new LinuxPlatform(env));
    }
}
