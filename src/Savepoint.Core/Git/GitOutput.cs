namespace Savepoint.Core.Git;

public static class GitOutput
{
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
