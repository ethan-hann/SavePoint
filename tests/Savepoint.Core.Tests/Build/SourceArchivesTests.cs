using System.Text.Json;
using System.Text.RegularExpressions;
using Savepoint.Testing;

namespace Savepoint.Core.Tests.Build;

// The notices promise the source of the exact bundles a release ships. The manifest that names the binaries also names those sources and
// pins their hashes, so the package scripts take what they download and compare from one place.
public partial class SourceArchivesTests
{
    private static readonly string[] _names = ["dugite-native", "git", "git-for-windows"];

    private static string RepoRoot => TestEnvironment.RepoRoot ?? throw new InvalidOperationException("These tests read the repository's build files.");

    private static JsonElement Manifest()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(GitManifestFile.ManifestPath));
        return document.RootElement.Clone();
    }

    private static IReadOnlyDictionary<string, (string Repository, string Tag, string Sha256)> Sources()
    {
        var manifest = Manifest();
        Assert.True(manifest.TryGetProperty("sources", out var sources), "build/git-manifest.json has no \"sources\" section.");
        return sources.EnumerateObject().ToDictionary(
            source => source.Name,
            source => (
                source.Value.GetProperty("repository").GetString()!,
                source.Value.GetProperty("tag").GetString()!,
                source.Value.GetProperty("sha256").GetString()!));
    }

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256Pattern();

    [Fact]
    public void The_manifest_names_the_source_of_git_git_for_windows_and_dugite_native()
    {
        var sources = Sources();

        Assert.Equal(_names, sources.Keys.Order(StringComparer.Ordinal));
        Assert.All(sources.Values, source =>
        {
            Assert.StartsWith("https://github.com/", source.Repository, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(source.Tag));
        });
    }

    [Fact]
    public void The_notices_promise_exactly_the_tags_the_manifest_pins()
    {
        var notices = File.ReadAllText(Path.Combine(RepoRoot, "THIRD-PARTY-NOTICES.md"));

        foreach (var (name, source) in Sources())
        {
            Assert.True(
                notices.Contains($"{source.Repository}, tag `{source.Tag}`", StringComparison.Ordinal),
                $"THIRD-PARTY-NOTICES.md does not promise source for {name} at {source.Repository}, tag {source.Tag}.");
        }
    }

    [Fact]
    public void The_pinned_tags_of_the_two_bundles_are_the_versions_the_bundles_are_fetched_at()
    {
        var manifest = Manifest();
        var sources = Sources();

        Assert.Contains(sources["git-for-windows"].Tag, manifest.GetProperty("win-x64").GetProperty("url").GetString(), StringComparison.Ordinal);
        Assert.Contains(sources["dugite-native"].Tag, manifest.GetProperty("linux-x64").GetProperty("url").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_hash_that_is_pinned_is_a_lowercase_sha256()
    {
        foreach (var (name, source) in Sources())
        {
            Assert.True(
                source.Sha256.Length == 0 || Sha256Pattern().IsMatch(source.Sha256),
                $"The pinned hash of {name} is not 64 lowercase hex digits.");
        }
    }

    [Theory]
    [InlineData("build/package.ps1")]
    [InlineData("build/package.sh")]
    public void Both_package_scripts_take_the_source_tags_from_the_manifest_and_not_from_the_notices(string script)
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot, script));

        Assert.Contains("git-manifest.json", text, StringComparison.Ordinal);
        Assert.Contains("sources", text, StringComparison.Ordinal);
        Assert.DoesNotContain("tags the notices promise source for", text, StringComparison.Ordinal);
    }
}
