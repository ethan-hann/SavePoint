namespace Savepoint.Core.Git;

/// <summary>Helpers for reading what git prints.</summary>
public static class GitOutput
{
    /// <summary>
    /// Splits the output of a <c>-z</c> command into its entries. Every entry ends with a NUL, so the empty piece after the
    /// last one is dropped; empty text has no entries.
    /// </summary>
    /// <param name="text">The whole decoded output; splitting it at once keeps a name from breaking across read chunks.</param>
    /// <returns>The entries, without their terminators.</returns>
    public static IReadOnlyList<string> SplitNul(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            return [];
        }

        var parts = text.Split('\0');

        // -z ends every entry with a NUL, so the last piece after the split is empty.
        return text[^1] == '\0' ? parts[..^1] : parts;
    }
}
