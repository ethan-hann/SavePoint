using System.Globalization;
using System.Text;

namespace Savepoint.App.Features.Common.Search;

/// <summary>What one list entry is searched by: its text with case, accents and apostrophes gone, plus a run-together form and the name's initials.</summary>
public sealed class SearchTarget
{
    // Fields are kept apart by a line break, which a typed word never holds, so a word is found inside one field only.
    private const char _fieldBreak = '\n';

    // The folded name a target built by ForName leads with; ranking looks at its words, found on first use.
    private readonly string? _foldedName;
    private string[]? _nameWords;
    private string[]? _nameTails;

    private SearchTarget(string text, string squashed, string? foldedName = null)
    {
        Text = text;
        Squashed = squashed;
        _foldedName = foldedName;
    }

    internal string Text { get; }

    internal string Squashed { get; }

    // The name's words; empty for a target that is not a name.
    internal string[] NameWords => _nameWords ??= _foldedName is null ? [] : [.. WordsOf(_foldedName)];

    // For each name word, the letters and digits from there to the end of the name, so "halflife" and "life" both start one.
    internal string[] NameTails => _nameTails ??= [.. NameWords.Select((_, index) => string.Concat(NameWords.Skip(index)))];

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
        return new SearchTarget(string.Join(_fieldBreak, folded, initials), string.Join(_fieldBreak, Squash(folded), initials), folded);
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

    internal static IEnumerable<string> WordsOf(string folded) =>
        folded.Split([.. folded.Where(letter => !char.IsLetterOrDigit(letter)).Distinct()], StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>What was typed in a search box: words that must each be found somewhere in an entry, in any order.</summary>
public sealed class SearchQuery
{
    private readonly (string Word, string Squashed)[] _words;

    // The typed words split at punctuation, and the letters of all of them run together: what ranking compares to a name's start.
    private readonly string[] _rankWords;
    private readonly string _rankJoined;

    private SearchQuery((string Word, string Squashed)[] words)
    {
        _words = words;
        _rankWords = [.. words.SelectMany(entry => SearchTarget.WordsOf(entry.Word))];
        _rankJoined = string.Concat(_rankWords);
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

    /// <summary>
    /// How well a matching name fits, lower being better: 0 when the name starts with what was typed, 1 when its first word starts with the
    /// first typed word, 2 otherwise (including a match through initials or from the middle of a word).
    /// </summary>
    /// <param name="target">A name's key, from <see cref="SearchTarget.ForName"/>; any other key ranks 2.</param>
    public int Rank(SearchTarget target)
    {
        var tails = target.NameTails;
        if (tails.Length == 0 || _rankWords.Length == 0)
        {
            return 2;
        }

        if (tails[0].StartsWith(_rankJoined, StringComparison.Ordinal))
        {
            return 0;
        }

        return target.NameWords[0].StartsWith(_rankWords[0], StringComparison.Ordinal) ? 1 : 2;
    }
}
