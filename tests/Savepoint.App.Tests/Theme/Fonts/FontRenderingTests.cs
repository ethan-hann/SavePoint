using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using Avalonia.VisualTree;
using Savepoint.App.Theme;

namespace Savepoint.App.Tests.Theme.Fonts;

public class FontRenderingTests
{
    [AvaloniaFact]
    public void Icon_text_block_is_drawn_by_the_symbols_font()
    {
        var block = IconBlock(Icons.PlayArrow, "SymbolsFont");

        block.Measure(Size.Infinity);

        Assert.True(block.DesiredSize.Width > 0);
        var run = Assert.Single(ShapedRuns(block));
        Assert.Equal(FontTestSupport.SymbolsFamily, run.GlyphRun.GlyphTypeface.FamilyName);
        Assert.NotEqual(0, run.GlyphRun.GlyphInfos[0].GlyphIndex);
    }

    // Control for the test above: a code point the subset dropped must not look like a real icon.
    [AvaloniaFact]
    public void Unlisted_code_point_is_not_drawn_as_an_icon()
    {
        var block = IconBlock(FontTestSupport.UnlistedIcon, "SymbolsFont");

        block.Measure(Size.Infinity);

        var drawnAsIcon = ShapedRuns(block).Any(run =>
            run.GlyphRun.GlyphTypeface.FamilyName == FontTestSupport.SymbolsFamily && run.GlyphRun.GlyphInfos[0].GlyphIndex != 0);
        Assert.False(drawnAsIcon);
    }

    [AvaloniaFact]
    public void Filled_font_draws_more_ink_than_the_outline_font()
    {
        var outline = Ink(Icons.Bookmark, "SymbolsFont");
        var filled = Ink(Icons.Bookmark, "SymbolsFilledFont");

        Assert.True(outline > 0, "The outline bookmark drew nothing.");
        Assert.True(filled > outline * 1.5, $"Filled ink {filled} is not clearly above outline ink {outline}.");
    }

    [AvaloniaFact]
    public void Default_font_family_is_the_embedded_roboto()
    {
        var family = FontManager.Current.DefaultFontFamily;

        Assert.Equal("Roboto", family.Name);
        var glyphTypeface = FontTestSupport.Resolve(family);
        Assert.Equal("Roboto", glyphTypeface.FamilyName);
        Assert.Equal(FontTestSupport.HashOfEmbeddedFile("Roboto-Regular.ttf"), FontTestSupport.HashOfResolvedFile(glyphTypeface));
    }

    [AvaloniaFact]
    public void Controls_that_set_no_font_are_drawn_in_the_embedded_roboto()
    {
        var window = new Window
        {
            Content = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = "Keep the setup that works" },
                    new Button { Content = "Play" },
                },
            },
        };
        window.Show();
        var expected = FontTestSupport.HashOfEmbeddedFile("Roboto-Regular.ttf");

        var blocks = window.GetVisualDescendants().OfType<TextBlock>().ToList();

        Assert.True(blocks.Count >= 2, "Expected the text block and the button's text.");
        foreach (var block in blocks)
        {
            var run = Assert.Single(ShapedRuns(block));
            Assert.Equal("Roboto", run.GlyphRun.GlyphTypeface.FamilyName);
            Assert.Equal(expected, FontTestSupport.HashOfResolvedFile(run.GlyphRun.GlyphTypeface));
        }
    }

    private static TextBlock IconBlock(string icon, string fontKey) => new()
    {
        Text = icon,
        FontFamily = FontTestSupport.Resource(fontKey),
        FontSize = 48,
        Foreground = Brushes.Black,
    };

    private static List<ShapedTextRun> ShapedRuns(TextBlock block) =>
        block.TextLayout.TextLines.SelectMany(line => line.TextRuns).OfType<ShapedTextRun>().ToList();

    private static long Ink(string icon, string fontKey)
    {
        var block = IconBlock(icon, fontKey);
        block.Measure(Size.Infinity);
        block.Arrange(new Rect(block.DesiredSize));
        var size = new PixelSize((int)Math.Ceiling(block.DesiredSize.Width), (int)Math.Ceiling(block.DesiredSize.Height));

        using var bitmap = new RenderTargetBitmap(size);
        bitmap.Render(block);

        var stride = size.Width * 4;
        var pixels = new byte[stride * size.Height];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(), pixels.Length, stride);
        }
        finally
        {
            handle.Free();
        }

        long ink = 0;
        for (var i = 3; i < pixels.Length; i += 4)
        {
            ink += pixels[i];
        }

        return ink;
    }
}
