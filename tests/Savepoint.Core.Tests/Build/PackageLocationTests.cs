using System.Xml.Linq;
using Savepoint.Core.Platform.Windows;
using Savepoint.Core.Tests.Platform;
using Savepoint.Testing;

namespace Savepoint.Core.Tests.Build;

// The Windows setup owns %LOCALAPPDATA%\<pack id> and deletes all of it on uninstall, so SavePoint's data must live elsewhere.
public class PackageLocationTests
{
    private const string _localAppData = @"C:\Users\Test\AppData\Local";

    private static string RepoRoot => TestEnvironment.RepoRoot ?? throw new InvalidOperationException("These tests read the repository's build files.");

    // Both the current pack id and SavePointApp, the id earlier Windows builds were packed with.
    [Fact]
    public void Nothing_SavePoint_keeps_lives_in_a_folder_the_installer_removes_on_install_or_uninstall()
    {
        var directories = new WindowsPlatform(FakeEnvironment.Windows().Build()).Directories;

        foreach (var packId in new[] { PackId(), "SavePointApp" })
        {
            var installRoot = $@"{_localAppData}\{packId}\";
            foreach (var folder in new[] { directories.CatalogDir, directories.DefaultStorageRoot, directories.LogsDir, directories.RuntimeDir })
            {
                Assert.False(
                    (folder + @"\").StartsWith(installRoot, StringComparison.OrdinalIgnoreCase),
                    $"{folder} is inside {installRoot}, which installing or uninstalling SavePoint deletes");
            }
        }
    }

    [Theory]
    [InlineData("build/package.ps1")]
    [InlineData("build/package.sh")]
    public void Each_package_script_takes_its_pack_id_from_the_build_properties(string script)
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot, script));

        Assert.Contains("SavepointPackId", text, StringComparison.Ordinal);
        Assert.DoesNotContain("SavepointWindowsPackId", text, StringComparison.Ordinal);
        Assert.DoesNotContain("--packId SavePoint ", text, StringComparison.Ordinal);
    }

    // One id names the app on every platform: the installer's folder, the AppImage, its desktop entry and the window class the shell shows.
    [Fact]
    public void One_pack_id_serves_both_platforms_and_is_the_app_name()
    {
        var props = XDocument.Load(Path.Combine(RepoRoot, "Directory.Build.props"));

        Assert.Equal("SavePoint", PackId());
        Assert.Empty(props.Descendants("SavepointWindowsPackId"));
    }

    // Installs made from the earlier id's setup still sit there, and storage moved into one would go with it.
    [Fact]
    public void The_platform_still_refuses_the_folder_the_earlier_Windows_setup_installed_into()
    {
        var directories = new WindowsPlatform(FakeEnvironment.Windows().Build()).Directories;

        Assert.Equal($@"{_localAppData}\SavePointApp", directories.InstallerLocalDir);
    }

    private static string PackId() =>
        XDocument.Load(Path.Combine(RepoRoot, "Directory.Build.props")).Descendants("SavepointPackId").Single().Value.Trim();
}
