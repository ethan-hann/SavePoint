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

    // Both ids: the one the Windows setup is packed with now, and the app name it is meant to take on.
    [Fact]
    public void Nothing_SavePoint_keeps_lives_in_a_folder_the_installer_removes_on_install_or_uninstall()
    {
        var directories = new WindowsPlatform(FakeEnvironment.Windows().Build()).Directories;

        foreach (var packId in new[] { WindowsPackId(), LinuxPackId() }.Distinct(StringComparer.OrdinalIgnoreCase))
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
    [InlineData("build/package.ps1", "SavepointWindowsPackId")]
    [InlineData("build/package.sh", "SavepointPackId")]
    public void Each_package_script_takes_its_pack_id_from_the_build_properties(string script, string property)
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot, script));

        Assert.Contains(property, text, StringComparison.Ordinal);
        Assert.DoesNotContain("--packId SavePoint ", text, StringComparison.Ordinal);
    }

    // Builds packed as SavePointApp keep their data in %LOCALAPPDATA%\SavePoint. A setup packed as SavePoint renames and empties that folder
    // before the app starts, so the data would be gone before the app could move it out.
    [Fact]
    public void The_Windows_pack_id_is_not_the_folder_old_data_is_moved_out_of_until_every_install_has_moved()
    {
        Assert.False(string.Equals("SavePoint", WindowsPackId(), StringComparison.OrdinalIgnoreCase), "the setup would empty the folder old data is still in");
    }

    [Fact]
    public void The_platform_names_the_installer_folder_after_the_Windows_pack_id()
    {
        var directories = new WindowsPlatform(FakeEnvironment.Windows().Build()).Directories;

        Assert.Equal($@"{_localAppData}\{WindowsPackId()}", directories.InstallerLocalDir);
    }

    // The AppImage has no install folder that an update deletes, and its desktop entry and notifications use the pack id as the name people see.
    [Fact]
    public void The_pack_id_the_Linux_AppImage_carries_is_the_app_name()
    {
        Assert.Equal("SavePoint", LinuxPackId());
    }

    private static string WindowsPackId() =>
        XDocument.Load(Path.Combine(RepoRoot, "Directory.Build.props")).Descendants("SavepointWindowsPackId").Single().Value.Trim();

    private static string LinuxPackId() =>
        XDocument.Load(Path.Combine(RepoRoot, "Directory.Build.props")).Descendants("SavepointPackId").Single().Value.Trim();
}
