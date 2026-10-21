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
    public static (uint Top, uint Bottom) Gradient(string name, bool dark)
    {
        var palette = TonalPalette.FromHueAndChroma(Hue(name), dark ? _darkChroma : _lightChroma);
        return dark ? (palette.Tone(34), palette.Tone(24)) : (palette.Tone(84), palette.Tone(74));
    }
}
