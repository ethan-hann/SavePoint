using Savepoint.Core.Paths;

namespace Savepoint.Core.Platform;

/// <summary>Reads the library list out of Steam's <c>libraryfolders.vdf</c>.</summary>
internal static class SteamLibraryFile
{
    private const int _maxLibraries = 256;

    /// <summary>
    /// The absolute library folders the file lists, in file order, normalized for <paramref name="syntax"/>. Entries that are not
    /// absolute paths are dropped. A missing, empty, oversized, or malformed file gives an empty list; this never throws.
    /// </summary>
    public static IReadOnlyList<string> LibraryPaths(string? text, PathSyntax syntax)
    {
        var libraries = ValveKeyValues.ParseRoot(text, "libraryfolders")?.Children;
        if (libraries is null)
        {
            return [];
        }

        var found = new List<string>();
        foreach (var (key, entry) in libraries)
        {
            // Current files nest a block per library; clients before 2021 wrote the path as the value of a numbered key.
            var path = entry.Children is not null
                ? entry.TextOf("path")
                : key.Length > 0 && key.All(char.IsAsciiDigit) ? entry.Text : null;

            if (!string.IsNullOrWhiteSpace(path) && syntax.IsAbsolute(path))
            {
                var normalized = syntax.Normalize(path);
                if (!found.Contains(normalized, StringComparer.FromComparison(syntax.Comparison)))
                {
                    found.Add(normalized);
                }
            }

            if (found.Count >= _maxLibraries)
            {
                break;
            }
        }

        return found;
    }
}
