using System.Xml.Linq;
using Savepoint.Testing;

namespace Savepoint.Core.Tests.Build;

// Only the Windows-only GOG Galaxy reader opens SQLite, so the Linux and macOS packages must not carry its libraries.
public class SqlitePackagingTests
{
    private static readonly string[] _sqlitePackages =
    [
        "Microsoft.Data.Sqlite",
        "Microsoft.Data.Sqlite.Core",
        "SQLitePCLRaw.core",
        "SQLitePCLRaw.bundle_e_sqlite3",
        "SQLitePCLRaw.provider.e_sqlite3",
        "SQLitePCLRaw.lib.e_sqlite3",
    ];

    private static string RepoRoot => TestEnvironment.RepoRoot ?? throw new InvalidOperationException("These tests read the repository's project files.");

    private static IEnumerable<XElement> AppReferences() =>
        XDocument.Load(Path.Combine(RepoRoot, "src", "Savepoint.App", "Savepoint.App.csproj")).Descendants("PackageReference");

    [Theory]
    [InlineData("Microsoft.Data.Sqlite")]
    [InlineData("Microsoft.Data.Sqlite.Core")]
    [InlineData("SQLitePCLRaw.core")]
    [InlineData("SQLitePCLRaw.bundle_e_sqlite3")]
    [InlineData("SQLitePCLRaw.provider.e_sqlite3")]
    [InlineData("SQLitePCLRaw.lib.e_sqlite3")]
    public void The_app_drops_the_runtime_files_of_each_SQLite_package_on_Linux_and_macOS_only(string package)
    {
        var reference = AppReferences().SingleOrDefault(r => (string?)r.Attribute("Include") == package);

        Assert.NotNull(reference);
        Assert.Contains("runtime", (string?)reference.Attribute("ExcludeAssets") ?? "", StringComparison.Ordinal);
        Assert.Contains("native", (string?)reference.Attribute("ExcludeAssets") ?? "", StringComparison.Ordinal);

        // A Windows publish, or a build with no runtime identifier (the tests), must keep the library.
        var condition = (string?)reference.Attribute("Condition") ?? "";
        Assert.Contains("linux", condition, StringComparison.Ordinal);
        Assert.Contains("osx", condition, StringComparison.Ordinal);
        Assert.DoesNotContain("win", condition, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_pinned_SQLite_package_has_a_central_version()
    {
        var versions = XDocument.Load(Path.Combine(RepoRoot, "Directory.Packages.props")).Descendants("PackageVersion")
            .Select(v => (string?)v.Attribute("Include")).ToHashSet();

        foreach (var package in _sqlitePackages)
        {
            Assert.Contains(package, versions);
        }
    }
}
