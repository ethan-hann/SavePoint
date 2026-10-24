using System.Text;

namespace Savepoint.Core.Platform;

/// <summary>A value in Valve's key-value text: either plain text or a block of further pairs.</summary>
/// <param name="Text">The quoted text, or null for a block.</param>
/// <param name="Children">The pairs inside a block, or null for plain text.</param>
internal sealed record ValveValue(string? Text, List<KeyValuePair<string, ValveValue>>? Children)
{
    /// <summary>The first child with this key, ignoring case; null when this is not a block or has none.</summary>
    public ValveValue? Child(string key) =>
        Children?.FirstOrDefault(pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)).Value;

    /// <summary>The text of the first child with this key, or null when it is missing or is a block.</summary>
    public string? TextOf(string key) => Child(key)?.Text;
}

/// <summary>
/// Reads the key-value text Steam writes (<c>libraryfolders.vdf</c>, <c>appmanifest_*.acf</c>). Never throws: bad input gives null.
/// </summary>
internal static class ValveKeyValues
{
    // The real files are a few KB; the bounds keep a hostile or corrupt file from costing memory or stack.
    public const int MaxChars = 1 << 20;
    private const int _maxDepth = 16;

    /// <summary>
    /// The pairs of the whole file, in order. Null when the text is missing, empty, over <see cref="MaxChars"/>, or not well formed.
    /// </summary>
    public static List<KeyValuePair<string, ValveValue>>? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxChars)
        {
            return null;
        }

        return new Reader(text).ReadFile();
    }

    /// <summary>The block under a top-level key such as <c>AppState</c>, ignoring case; null when the text is bad or has no such block.</summary>
    public static ValveValue? ParseRoot(string? text, string key) =>
        Parse(text)?.FirstOrDefault(pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)).Value;

    private sealed class Reader(string text)
    {
        private int _at;

        public List<KeyValuePair<string, ValveValue>>? ReadFile() => ReadPairs(depth: 0, topLevel: true);

        private List<KeyValuePair<string, ValveValue>>? ReadPairs(int depth, bool topLevel)
        {
            if (depth > _maxDepth)
            {
                return null;
            }

            var pairs = new List<KeyValuePair<string, ValveValue>>();
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

                ValveValue value;
                if (text[_at] == '{')
                {
                    _at++;
                    if (ReadPairs(depth + 1, topLevel: false) is not { } block)
                    {
                        return null;
                    }

                    _at++; // the closing brace
                    value = new ValveValue(null, block);
                }
                else if (ReadQuoted() is { } plain)
                {
                    value = new ValveValue(plain, null);
                }
                else
                {
                    return null;
                }

                pairs.Add(new KeyValuePair<string, ValveValue>(key, value));
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
