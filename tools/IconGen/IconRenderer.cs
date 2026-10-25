using Savepoint.App.Theme;
using SkiaSharp;

namespace Savepoint.Tools.IconGen;

// Draws the app mark: a rounded square (radius a quarter of the side) in the light theme's primary with the bookmark glyph in onPrimary.
internal static class IconRenderer
{
    private const float _cornerRadius = 0.25f;
    private const float _glyphEm = 0.62f;
    private const int _snapBelow = 48;

    internal static byte[] Render(int side, SKTypeface outline, SKTypeface filled)
    {
        using var surface = SKSurface.Create(new SKImageInfo(side, side, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        using var square = new SKPaint { Color = new SKColor(IconColors.Primary), IsAntialias = true };
        var radius = side * _cornerRadius;
        canvas.DrawRoundRect(new SKRect(0, 0, side, side), radius, radius, square);

        using var font = new SKFont(IconFrames.UsesFilledGlyph(side) ? filled : outline, side * _glyphEm);
        var glyph = font.GetGlyphs(Icons.Bookmark)[0];
        if (glyph == 0)
        {
            throw new InvalidOperationException("The icon font has no bookmark glyph.");
        }

        // The glyph is centered by its ink, not its em box, so the mark stays centered if the font's metrics change.
        using var path = font.GetGlyphPath(glyph);
        var ink = path.TightBounds;
        var dx = (side / 2f) - ink.MidX;
        var dy = (side / 2f) - ink.MidY;
        if (side <= _snapBelow)
        {
            // Small sizes put the ink's left and top edges on whole pixels, so a hairline stroke is crisp rather than split over two pixels.
            dx = MathF.Round(ink.Left + dx) - ink.Left;
            dy = MathF.Round(ink.Top + dy) - ink.Top;
        }

        path.Transform(SKMatrix.CreateTranslation(dx, dy));

        using var glyphPaint = new SKPaint { Color = new SKColor(IconColors.OnPrimary), IsAntialias = true };
        canvas.DrawPath(path, glyphPaint);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
