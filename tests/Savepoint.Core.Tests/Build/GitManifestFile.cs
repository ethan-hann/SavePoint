using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Savepoint.Testing;

namespace Savepoint.Core.Tests.Build;

// Reads build/git-manifest.json on its own, so the tests do not trust the fetch scripts' reading of it.
internal static class GitManifestFile
{
    internal static string BuildFolder => Path.Combine(TestEnvironment.RepoRoot ?? throw new InvalidOperationException("The tests did not run inside a checkout."), "build");

    internal static string ManifestPath => Path.Combine(BuildFolder, "git-manifest.json");

    internal static IReadOnlyList<string> PrunePatterns(string layout)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(ManifestPath));
        return document.RootElement.GetProperty("layouts").GetProperty(layout).GetProperty("prune")
            .EnumerateArray()
            .Select(element => element.GetString()!)
            .ToArray();
    }

    internal static string Entry(string runtimeId)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(ManifestPath));
        return document.RootElement.GetProperty(runtimeId).GetProperty("entry").GetString()!;
    }

    // The stamp records this, so editing the list makes every existing extraction run again.
    internal static string PruneHash(IEnumerable<string> patterns) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Concat(patterns.Select(pattern => pattern + "\n")))));

    // * and ? stay inside one path segment; the whole relative path (forward slashes) has to match.
    internal static bool Matches(string pattern, string relativePath, bool ignoreCase)
    {
        var expression = "^" + Regex.Escape(pattern).Replace(@"\*", "[^/]*").Replace(@"\?", "[^/]") + "$";
        return Regex.IsMatch(relativePath, expression, ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
    }

    internal static IEnumerable<string> RelativeEntries(string root)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = false };
        return Directory.EnumerateFileSystemEntries(root, "*", options)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'));
    }
}
