using System.Globalization;
using System.Text;

namespace Savepoint.App.Features.Common.Search;

/// <summary>What one list entry is searched by: its text with case, accents and apostrophes gone, plus a run-together form and the name's initials.</summary>
public sealed class SearchTarget
{
    // Fields are kept apart by a line break, which a typed word never holds, so a word is found inside one field only.
    private const char _fieldBreak = '\n';

    private SearchTarget(string text, string squashed)
    {
        Text = text;
        Squashed = squashed;
    }

    internal string Text { get; }

    internal string Squashed { get; }

    /// <summary>Builds a key from the fields an entry can be found by.</summary>
    /// <param name="fields">The entry's name, notes, dates and so on; null or empty fields are skipped.</param>
    public static SearchTarget Of(params string?[] fields)
    {
        var plain = fields.Where(field => !string.IsNullOrEmpty(field)).Select(field => Fold(field!)).ToList();
        return new SearchTarget(string.Join(_fieldBreak, plain), string.Join(_fieldBreak, plain.Select(Squash)));
    }

    /// <summary>Builds a key for a game's name. The initials of its words also count, so "skyrim se" finds "Skyrim Special Edition".</summary>
    /// <param name="name">The name.</param>
    public static SearchTarget ForName(string name)
    {
        var folded = Fold(name);
        var initials = new string([.. WordsOf(folded).Select(word => word[0])]);
        return new SearchTarget(string.Join(_fieldBreak, folded, initials), string.Join(_fieldBreak, Squash(folded), initials));
    }

    // Lowercase, accents removed, apostrophes dropped: the same treatment for what is typed and what is searched.
    internal static string Fold(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var folded = new StringBuilder(decomposed.Length);
        foreach (var letter in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(letter) != UnicodeCategory.NonSpacingMark && letter is not ('\'' or '’' or '‘'))
            {
                folded.Append(letter);
            }
        }

        return folded.ToString().ToLowerInvariant();
    }

    // Letters and digits only, so "spiderman" finds "Spider-Man".
    internal static string Squash(string folded) => new([.. folded.Where(char.IsLetterOrDigit)]);

    private static IEnumerable<string> WordsOf(string folded) =>
        folded.Split([.. folded.Where(letter => !char.IsLetterOrDigit(letter)).Distinct()], StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>What was typed in a search box: words that must each be found somewhere in an entry, in any order.</summary>
public sealed class SearchQuery
{
    private readonly (string Word, string Squashed)[] _words;

    private SearchQuery((string Word, string Squashed)[] words)
    {
        _words = words;
    }

    /// <summary>A query that matches every entry.</summary>
    public static SearchQuery Empty { get; } = new([]);

    /// <summary>True when nothing was typed, so every entry matches.</summary>
    public bool IsEmpty => _words.Length == 0;

    /// <summary>Reads what was typed.</summary>
    /// <param name="text">The search box's text.</param>
    public static SearchQuery Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Empty;
        }

        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(SearchTarget.Fold)
            .Where(word => word.Length > 0)
            .Select(word => (word, SearchTarget.Squash(word)))
            .ToArray();
        return words.Length == 0 ? Empty : new SearchQuery(words);
    }

    /// <summary>True when every typed word is found in the entry.</summary>
    /// <param name="target">What the entry is searched by.</param>
    public bool Matches(SearchTarget target) =>
        _words.All(entry => target.Text.Contains(entry.Word, StringComparison.Ordinal)
            || (entry.Squashed.Length > 0 && target.Squashed.Contains(entry.Squashed, StringComparison.Ordinal)));
}
