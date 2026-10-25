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

    [Fact]
    public void Nothing_SavePoint_keeps_lives_in_the_folder_the_installer_removes_on_uninstall()
    {
        var installRoot = $@"{_localAppData}\{PackId()}\";
        var directories = new WindowsPlatform(FakeEnvironment.Windows().Build()).Directories;

        foreach (var folder in new[] { directories.CatalogDir, directories.DefaultStorageRoot, directories.LogsDir, directories.RuntimeDir })
        {
            Assert.False(
                (folder + @"\").StartsWith(installRoot, StringComparison.OrdinalIgnoreCase),
                $"{folder} is inside {installRoot}, which uninstalling SavePoint deletes");
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

    // Everywhere else the app is just "SavePoint", but that name on Windows would make the setup own the data folder.
    [Fact]
    public void The_Windows_pack_id_is_not_the_name_of_SavePoints_data_folders()
    {
        Assert.False(string.Equals("SavePoint", PackId(), StringComparison.OrdinalIgnoreCase), "the setup would own the folder SavePoint keeps its data in");
    }

    // The AppImage has no install folder that an update deletes, and its desktop entry and notifications use the pack id as the name people see.
    [Fact]
    public void The_pack_id_the_Linux_AppImage_carries_is_the_app_name()
    {
        Assert.Equal("SavePoint", XDocument.Load(Path.Combine(RepoRoot, "Directory.Build.props")).Descendants("SavepointPackId").Single().Value.Trim());
    }

    private static string PackId() =>
        XDocument.Load(Path.Combine(RepoRoot, "Directory.Build.props")).Descendants("SavepointWindowsPackId").Single().Value.Trim();
}
