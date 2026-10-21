using Savepoint.Core.Paths;
using Savepoint.Core.Platform;

namespace Savepoint.Core.Tests.Paths;

// The Windows rules for dots and spaces are emulated so another host can check them; this pins the emulation to the real thing.
public class PathSyntaxHostTests
{
    private static readonly string[] _alphabet = ["a", ".", " "];

    [Fact]
    public void Normalize_matches_the_windows_full_path_for_every_short_spelling_of_a_folder_name()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Compares with the Windows path rules.");
        var syntax = PathSyntax.For(OsKind.Windows);
        var mismatches = new List<string>();

        foreach (var name in Spellings(maxLength: 4))
        {
            foreach (var path in new[] { $@"C:\x\{name}\y", $@"C:\x\{name}", $@"C:\x\{name}\", $@"C:\x\{name}\..\y", $@"C:\x\a\{name}\..\y" })
            {
                var expected = TrimTrailingSeparator(Path.GetFullPath(path));
                var actual = syntax.Normalize(path);
                if (!string.Equals(expected, actual, StringComparison.Ordinal))
                {
                    mismatches.Add($"[{path}] windows: [{expected}] emulated: [{actual}]");
                }
            }
        }

        Assert.True(mismatches.Count == 0, string.Join(Environment.NewLine, mismatches.Take(15)) + $"{Environment.NewLine}({mismatches.Count} mismatches)");
    }

    private static string TrimTrailingSeparator(string path) =>
        path.Length > 3 ? path.TrimEnd('\\') : path;

    private static IEnumerable<string> Spellings(int maxLength)
    {
        IEnumerable<string> current = [string.Empty];
        for (var length = 1; length <= maxLength; length++)
        {
            current = current.SelectMany(prefix => _alphabet.Select(letter => prefix + letter)).ToList();
            foreach (var spelling in current)
            {
                yield return spelling;
            }
        }
    }
}
