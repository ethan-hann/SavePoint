namespace Savepoint.Tools.IconGen;

// Which face of the icon font draws the glyph at a given size.
internal static class IconFrames
{
    // The outline's strokes are about a pixel wide at small sizes and blur; the filled glyph stays solid there.
    internal const int FilledUpTo = 32;

    internal static bool UsesFilledGlyph(int side) => side <= FilledUpTo;
}
