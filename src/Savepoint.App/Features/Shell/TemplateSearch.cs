using System.Text.RegularExpressions;

namespace Savepoint.App.Features.Shell;

/// <summary>What a template's name is searched by: its words, and the run of letters from each word to the end.</summary>
/// <param name="Words">The name's lowercase words, such as "half" and "life".</param>
/// <param name="Tails">For each word, every letter from there to the end with the punctuation gone, so "halflife" and "life" both start one.</param>
internal sealed record SearchKey(string[] Words, string[] Tails);

/// <summary>How typed words find a template by name; the template picker and the editor's Name field share it.</summary>
internal static partial class TemplateSearch
{
    /// <summary>Lowercase words with apostrophes dropped and other punctuation treated as a space.</summary>
    /// <param name="text">A typed search or a template's name.</param>
    public static string[] WordsOf(string text) =>
        [.. NotALetterOrDigit().Split(Apostrophes().Replace(text.ToLowerInvariant(), string.Empty)).Where(word => word.Length > 0)];

    /// <summary>Builds what a name is searched by.</summary>
    /// <param name="name">A template's name.</param>
    public static SearchKey KeyOf(string name)
    {
        var words = WordsOf(name);
        return new SearchKey(words, [.. words.Select((_, index) => string.Concat(words.Skip(index)))]);
    }

    /// <summary>
    /// True when every typed word starts a word of the name, or starts a run of words with the punctuation between them left out,
    /// so "sid meiers civ" finds "Sid Meier's Civilization VI" and "spiderman" finds "Spider-Man".
    /// </summary>
    /// <param name="typed">The words typed, from <see cref="WordsOf"/>.</param>
    /// <param name="key">The name's key, from <see cref="KeyOf"/>.</param>
    public static bool Matches(string[] typed, SearchKey key) =>
        typed.All(word => key.Tails.Any(tail => tail.StartsWith(word, StringComparison.Ordinal)));

    /// <summary>How well a match fits: 0 when the name starts with what was typed, 1 when its first word starts with the first typed word, 2 otherwise.</summary>
    /// <param name="typed">The words typed, from <see cref="WordsOf"/>; at least one.</param>
    /// <param name="key">The name's key, from <see cref="KeyOf"/>.</param>
    public static int Rank(string[] typed, SearchKey key)
    {
        if (key.Tails.Length == 0)
        {
            return 2;
        }

        if (key.Tails[0].StartsWith(string.Concat(typed), StringComparison.Ordinal))
        {
            return 0;
        }

        return key.Words[0].StartsWith(typed[0], StringComparison.Ordinal) ? 1 : 2;
    }

    /// <summary>The items that match what was typed, best fit first and otherwise in the given order; every item when no word was typed.</summary>
    /// <typeparam name="T">What is searched.</typeparam>
    /// <param name="all">The items with their keys, in the order shown when nothing is typed.</param>
    /// <param name="text">What was typed.</param>
    public static IEnumerable<T> Find<T>(IEnumerable<(T Item, SearchKey Key)> all, string text)
    {
        var typed = WordsOf(text);
        return typed.Length == 0
            ? all.Select(entry => entry.Item)
            : all.Where(entry => Matches(typed, entry.Key)).OrderBy(entry => Rank(typed, entry.Key)).Select(entry => entry.Item);
    }

    [GeneratedRegex(@"['’]")]
    private static partial Regex Apostrophes();

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NotALetterOrDigit();
}
