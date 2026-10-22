using System.Globalization;
using Savepoint.App.Theme.Material;

namespace Savepoint.App.Features.Common.Art;

/// <summary>
/// The tint of a game's placeholder art. There is no artwork: the tile is a gradient whose hue comes from a hash of
/// the game's name, drawn at tones that suit the mode so it sits quietly on the surface in both.
/// </summary>
public static class ArtTint
{
    private const double _darkChroma = 16;
    private const double _lightChroma = 26;

    private static readonly string[] _articles = ["the", "a", "an"];

    /// <summary>
    /// A hue from 0 to 359 for a name, by the hash the original app used: <c>hash = hash * 31 + char</c> in 32-bit
    /// arithmetic over UTF-16 code units, then the absolute value modulo 360. The same name is always the same hue.
    /// </summary>
    public static int Hue(string name)
    {
        var hash = 0;
        foreach (var unit in name)
        {
            hash = unchecked((hash * 31) + unit);
        }

        return (int)(Math.Abs((long)hash) % 360);
    }

    /// <summary>The two colors of the tile's top-to-bottom gradient, as opaque ARGB values; the top is the lighter one.</summary>
    public static (uint Top, uint Bottom) Gradient(string name, bool dark) => Gradient(Hue(name), dark);

    /// <summary>The gradient for a hue from 0 to 359.</summary>
    public static (uint Top, uint Bottom) Gradient(int hue, bool dark)
    {
        var palette = TonalPalette.FromHueAndChroma(hue, dark ? _darkChroma : _lightChroma);
        return dark ? (palette.Tone(34), palette.Tone(24)) : (palette.Tone(84), palette.Tone(74));
    }

    /// <summary>The color of the initials drawn over the gradient of a hue; 4.5:1 or better against both of its colors.</summary>
    public static uint InitialsColor(int hue, bool dark) =>
        TonalPalette.FromHueAndChroma(hue, dark ? _darkChroma : _lightChroma).Tone(dark ? 95 : 15);

    /// <summary>
    /// One or two letters that stand for a game name where the name itself has no room: the first letters of its first two words
    /// ("Skyrim Special Edition" gives "SS"), or the first two letters of a single word ("Satisfactory" gives "Sa"). A leading
    /// "The", "A" or "An" is skipped when more words follow. A blank name gives an empty string.
    /// </summary>
    public static string Initials(string name)
    {
        var words = name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(LettersFrom)
            .Where(letters => letters.Count > 0)
            .ToList();
        if (words.Count > 1 && _articles.Contains(string.Concat(words[0]), StringComparer.OrdinalIgnoreCase))
        {
            words.RemoveAt(0);
        }

        return words.Count switch
        {
            0 => string.Empty,
            1 => string.Concat(words[0][0].ToUpperInvariant(), words[0].Count > 1 ? words[0][1].ToLowerInvariant() : string.Empty),
            _ => string.Concat(words[0][0].ToUpperInvariant(), words[1][0].ToUpperInvariant()),
        };
    }

    // The word's characters from its first letter or digit on, one string per character as a person sees it, so a pair of UTF-16 units is never split.
    private static List<string> LettersFrom(string word)
    {
        var letters = new List<string>();
        var elements = StringInfo.GetTextElementEnumerator(word);
        while (elements.MoveNext())
        {
            var element = (string)elements.Current;
            if (letters.Count > 0 || char.IsLetterOrDigit(element, 0))
            {
                letters.Add(element);
            }
        }

        return letters;
    }
}
