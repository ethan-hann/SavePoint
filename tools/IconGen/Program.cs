using Savepoint.App.Theme;
using SkiaSharp;

namespace Savepoint.Tools.IconGen;

// Draws the app mark and writes the icon files into src/Savepoint.App/Assets/Icon. Run it by hand when the design changes:
//   dotnet run --project tools/IconGen -c Release
// The mark is a rounded square (radius a quarter of the side) in the light theme's primary with the bookmark glyph in onPrimary.
internal static class Program
{
    private const float _cornerRadius = 0.25f;
    private const float _glyphEm = 0.62f;
    private const int _snapBelow = 48;

    private static readonly int[] _icoSizes = [16, 24, 32, 48, 64, 128, 256];
    private static readonly int[] _pngSizes = [256, 512, 1024];

    private static int Main(string[] args)
    {
        var root = FindRepoRoot();
        if (root is null)
        {
            Console.Error.WriteLine("IconGen: run it from inside the SavePoint checkout.");
            return 2;
        }

        var assets = Path.Combine(root, "src", "Savepoint.App", "Assets");
        var output = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(assets, "Icon");
        Directory.CreateDirectory(output);

        using var typeface = SKTypeface.FromFile(Path.Combine(assets, "Fonts", "MaterialSymbolsRounded.ttf"))
            ?? throw new InvalidOperationException("The icon font could not be loaded.");

        File.WriteAllBytes(Path.Combine(output, "savepoint.ico"), IcoFile.Build(_icoSizes.Select(size => (size, Render(size, typeface))).ToArray()));
        foreach (var size in _pngSizes)
        {
            File.WriteAllBytes(Path.Combine(output, $"savepoint-{size}.png"), Render(size, typeface));
        }

        Console.WriteLine($"Wrote the icon files to {output}");
        return 0;
    }

    private static byte[] Render(int side, SKTypeface typeface)
    {
        using var surface = SKSurface.Create(new SKImageInfo(side, side, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        using var square = new SKPaint { Color = new SKColor(IconColors.Primary), IsAntialias = true };
        var radius = side * _cornerRadius;
        canvas.DrawRoundRect(new SKRect(0, 0, side, side), radius, radius, square);

        using var font = new SKFont(typeface, side * _glyphEm);
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

    private static string? FindRepoRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Savepoint.slnx")))
                {
                    return directory.FullName;
                }
            }
        }

        return null;
    }
}
