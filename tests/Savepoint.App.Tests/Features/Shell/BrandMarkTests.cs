using Avalonia.Headless.XUnit;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Theme;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Shell;

public class BrandMarkTests
{
    // The glyph's ink is whatever differs between the mark as drawn and the mark with its glyph hidden, which leaves the rounded corners out of it.
    private static (double X, double Y) InkCenter(BrandMark mark, ThemeHost host, string? savePath)
    {
        var frame = ThemeTestSupport.Render(host.Window);
        if (savePath is not null)
        {
            ThemeTestSupport.SaveFrame(frame, savePath);
        }

        var glyph = mark.Child ?? throw new InvalidOperationException("The mark has no glyph.");
        glyph.Opacity = 0;
        var bare = ThemeTestSupport.Render(host.Window);

        // The mark sits alone at the top left of the window's content, so its bounds are its place in the frame.
        var origin = mark.Bounds.Position;
        var width = (int)mark.Width;
        var height = (int)mark.Height;
        var differences = new double[width, height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var pixel = ThemeTestSupport.PixelAt(frame, (int)origin.X + x, (int)origin.Y + y);
                var empty = ThemeTestSupport.PixelAt(bare, (int)origin.X + x, (int)origin.Y + y);
                differences[x, y] = Math.Abs(pixel.G - empty.G);
            }
        }

        var strongest = differences.Cast<double>().Max();
        var coverage = new double[width, height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                coverage[x, y] = differences[x, y] / strongest;
            }
        }

        // An edge sits inside its outermost touched pixel by that pixel's coverage, so the ink's bounds are known to a fraction of a pixel.
        double RowMax(int y) => Enumerable.Range(0, width).Max(x => coverage[x, y]);
        double ColumnMax(int x) => Enumerable.Range(0, height).Max(y => coverage[x, y]);
        var rows = Enumerable.Range(0, height).Where(y => RowMax(y) > 0.05).ToList();
        var columns = Enumerable.Range(0, width).Where(x => ColumnMax(x) > 0.05).ToList();
        Assert.True(rows.Count > 0 && columns.Count > 0, "The glyph did not draw.");
        var top = rows[0] + (1 - RowMax(rows[0]));
        var bottom = rows[^1] + RowMax(rows[^1]);
        var left = columns[0] + (1 - ColumnMax(columns[0]));
        var right = columns[^1] + ColumnMax(columns[^1]);
        return ((left + right) / 2, (top + bottom) / 2);
    }

    [AvaloniaTheory]
    [InlineData(24, 16)]
    [InlineData(28, 18)]
    [InlineData(48, 32)]
    public void The_glyph_is_centered_by_its_ink_within_a_quarter_of_a_pixel(int size, int glyph)
    {
        var mark = new BrandMark(size, glyph) { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        using var host = new ThemeHost(mark, ThemePreference.Light, width: 120, height: 120);

        var (x, y) = InkCenter(mark, host, Path.Combine(ThemeTestSupport.RepoRoot(), "render-output", "brand-mark", $"mark-{size}-{glyph}.png"));

        Assert.True(Math.Abs(x - (size / 2.0)) <= 0.25, $"The glyph's ink is centered {x:0.##} px across a {size} px mark.");
        Assert.True(Math.Abs(y - (size / 2.0)) <= 0.25, $"The glyph's ink is centered {y:0.##} px down a {size} px mark.");
    }
}
