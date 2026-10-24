using System.Text;
using Savepoint.Core.Paths;

namespace Savepoint.Core.Platform;

/// <summary>Reads the library list out of Steam's <c>libraryfolders.vdf</c>.</summary>
internal static class SteamLibraryFile
{
    // The real file is a few KB; the bounds keep a hostile or corrupt file from costing memory or stack.
    private const int _maxChars = 1 << 20;
    private const int _maxDepth = 16;
    private const int _maxLibraries = 256;

    /// <summary>
    /// The absolute library folders the file lists, in file order, normalized for <paramref name="syntax"/>. Entries that are not
    /// absolute paths are dropped. A missing, empty, oversized, or malformed file gives an empty list; this never throws.
    /// </summary>
    public static IReadOnlyList<string> LibraryPaths(string? text, PathSyntax syntax)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > _maxChars)
        {
            return [];
        }

        var top = new Reader(text).ReadFile();
        var libraries = top?.FirstOrDefault(pair => string.Equals(pair.Key, "libraryfolders", StringComparison.OrdinalIgnoreCase)).Value?.Children;
        if (libraries is null)
        {
            return [];
        }

        var found = new List<string>();
        foreach (var (key, entry) in libraries)
        {
            // Current files nest a block per library; clients before 2021 wrote the path as the value of a numbered key.
            var path = entry.Children is { } block
                ? block.FirstOrDefault(pair => string.Equals(pair.Key, "path", StringComparison.OrdinalIgnoreCase)).Value?.Text
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

    // A value is either text or a block of key/value pairs.
    private sealed record Node(string? Text, List<KeyValuePair<string, Node>>? Children);

    private sealed class Reader(string text)
    {
        private int _at;

        /// <summary>The pairs of the whole file, or null when it is not well formed.</summary>
        public List<KeyValuePair<string, Node>>? ReadFile() => ReadPairs(depth: 0, topLevel: true);

        private List<KeyValuePair<string, Node>>? ReadPairs(int depth, bool topLevel)
        {
            if (depth > _maxDepth)
            {
                return null;
            }

            var pairs = new List<KeyValuePair<string, Node>>();
            while (true)
            {
                SkipSpaceAndComments();
                if (_at >= text.Length)
                {
                    return topLevel ? pairs : null;
                }

                if (text[_at] == '}')
                {
                    return topLevel ? null : pairs;
                }

                if (ReadQuoted() is not { } key)
                {
                    return null;
                }

                SkipSpaceAndComments();
                if (_at >= text.Length)
                {
                    return null;
                }

                Node value;
                if (text[_at] == '{')
                {
                    _at++;
                    if (ReadPairs(depth + 1, topLevel: false) is not { } block)
                    {
                        return null;
                    }

                    _at++; // the closing brace
                    value = new Node(null, block);
                }
                else if (ReadQuoted() is { } plain)
                {
                    value = new Node(plain, null);
                }
                else
                {
                    return null;
                }

                pairs.Add(new KeyValuePair<string, Node>(key, value));
            }
        }

        private void SkipSpaceAndComments()
        {
            while (_at < text.Length)
            {
                if (char.IsWhiteSpace(text[_at]))
                {
                    _at++;
                }
                else if (text[_at] == '/' && _at + 1 < text.Length && text[_at + 1] == '/')
                {
                    while (_at < text.Length && text[_at] != '\n')
                    {
                        _at++;
                    }
                }
                else
                {
                    return;
                }
            }
        }

        private string? ReadQuoted()
        {
            if (_at >= text.Length || text[_at] != '"')
            {
                return null;
            }

            var value = new StringBuilder();
            _at++;
            while (_at < text.Length)
            {
                var c = text[_at++];
                if (c == '"')
                {
                    return value.ToString();
                }

                if (c != '\\' || _at >= text.Length)
                {
                    value.Append(c);
                    continue;
                }

                var escaped = text[_at++];
                value.Append(escaped switch
                {
                    'n' => "\n",
                    't' => "\t",
                    '\\' => "\\",
                    '"' => "\"",
                    _ => $"\\{escaped}",
                });
            }

            return null;
        }
    }
}
