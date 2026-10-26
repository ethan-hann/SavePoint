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

    // The restore keeps packages in the workspace folder nuget.config names; its default is used when the file does not say.
    private static string PackagesFolder()
    {
        // NuGet takes NUGET_PACKAGES over nuget.config, and the Linux gate sets it.
        if (Environment.GetEnvironmentVariable("NUGET_PACKAGES") is { Length: > 0 } fromEnvironment)
        {
            return Path.GetFullPath(fromEnvironment);
        }

        var configured = XDocument.Load(Path.Combine(RepoRoot, "nuget.config")).Descendants("add")
            .FirstOrDefault(add => (string?)add.Attribute("key") == "globalPackagesFolder");
        var folder = (string?)configured?.Attribute("value") ?? ".tools/nuget-packages";
        return Path.GetFullPath(Path.Combine(RepoRoot, folder));
    }

    private static Dictionary<string, string> Pins() =>
        XDocument.Load(Path.Combine(RepoRoot, "Directory.Packages.props")).Descendants("PackageVersion")
            .ToDictionary(v => (string)v.Attribute("Include")!, v => (string)v.Attribute("Version")!, StringComparer.OrdinalIgnoreCase);

    // A dependency range such as "[2.1.13, )" or "2.1.13" stands for its lower bound.
    private static string LowerBound(string range) => range.Trim('[', '(', ']', ')', ' ').Split(',')[0].Trim();

    [Fact]
    public void The_pins_equal_the_versions_the_pinned_packages_depend_on()
    {
        var pins = Pins();
        var folder = PackagesFolder();
        foreach (var package in _sqlitePackages)
        {
            var id = package.ToLowerInvariant();
            var nuspec = Path.Combine(folder, id, pins[package], id + ".nuspec");
            Assert.True(File.Exists(nuspec), $"{nuspec} is missing; restore the solution first.");

            var dependencies = XDocument.Load(nuspec).Descendants().Where(e => e.Name.LocalName == "dependency")
                .Select(e => (Id: (string)e.Attribute("id")!, Range: (string)e.Attribute("version")!))
                .Where(d => pins.ContainsKey(d.Id) && _sqlitePackages.Contains(d.Id, StringComparer.OrdinalIgnoreCase))
                .Distinct();
            foreach (var (dependency, range) in dependencies)
            {
                Assert.True(LowerBound(range) == pins[dependency], $"{package} {pins[package]} depends on {dependency} {range}, but the pin is {pins[dependency]}.");
            }
        }
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
