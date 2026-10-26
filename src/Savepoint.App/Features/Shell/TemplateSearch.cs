using System.Text.RegularExpressions;

namespace Savepoint.App.Features.Shell;

/// <summary>How typed words find a template by name; the template picker and the editor's Name field share it.</summary>
internal static partial class TemplateSearch
{
    /// <summary>Lowercase words with apostrophes dropped and other punctuation treated as a space.</summary>
    /// <param name="text">A typed search or a template's name.</param>
    public static string[] WordsOf(string text) =>
        [.. NotALetterOrDigit().Split(Apostrophes().Replace(text.ToLowerInvariant(), string.Empty)).Where(word => word.Length > 0)];

    /// <summary>True when every typed word starts a word of the name, so "sid meiers civ" finds "Sid Meier's Civilization VI".</summary>
    /// <param name="typed">The words typed, from <see cref="WordsOf"/>.</param>
    /// <param name="name">The words of a template's name, from <see cref="WordsOf"/>.</param>
    public static bool Matches(string[] typed, string[] name) =>
        typed.All(word => name.Any(candidate => candidate.StartsWith(word, StringComparison.Ordinal)));

    [GeneratedRegex(@"['’]")]
    private static partial Regex Apostrophes();

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NotALetterOrDigit();
}
