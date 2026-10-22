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
    [InlineData("build/package.ps1")]
    [InlineData("build/package.sh")]
    public void Both_package_scripts_take_the_pack_id_from_the_build_properties(string script)
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot, script));

        Assert.Contains("SavepointPackId", text, StringComparison.Ordinal);
        Assert.DoesNotContain("--packId SavePoint ", text, StringComparison.Ordinal);
    }

    private static string PackId() =>
        XDocument.Load(Path.Combine(RepoRoot, "Directory.Build.props")).Descendants("SavepointPackId").Single().Value.Trim();
}
