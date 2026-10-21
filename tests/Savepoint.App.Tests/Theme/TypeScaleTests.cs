using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;

namespace Savepoint.App.Tests.Theme;

// The type scale from the visual spec, as the TextBlock style classes screens use.
public class TypeScaleTests
{
    private static TextBlock Styled(string classes)
    {
        var block = new TextBlock { Text = "Sample" };
        foreach (var name in classes.Split(' '))
        {
            block.Classes.Add(name);
        }

        ThemeTestSupport.HostInWindow(block);
        return block;
    }

    [AvaloniaTheory]
    [InlineData("page-title", 24, 400)]
    [InlineData("dialog-title", 20, 400)]
    [InlineData("section-title", 18, 400)]
    [InlineData("card-title", 16, 500)]
    [InlineData("card-heading", 14.5, 500)]
    [InlineData("row-title", 13.5, 500)]
    [InlineData("body", 13, 400)]
    [InlineData("button-text", 12.5, 500)]
    [InlineData("caption", 12, 400)]
    [InlineData("timestamp", 11.5, 400)]
    [InlineData("overline", 11, 500)]
    [InlineData("overline-small", 10.5, 500)]
    public void Class_sets_the_size_and_weight_from_the_spec(string classes, double size, int weight)
    {
        var block = Styled(classes);

        Assert.Equal(size, block.FontSize);
        Assert.Equal((FontWeight)weight, block.FontWeight);
    }

    [AvaloniaTheory]
    [InlineData("overline")]
    [InlineData("overline-small")]
    public void Overline_labels_track_half_a_pixel(string classes)
    {
        Assert.Equal(0.5, Styled(classes).LetterSpacing);
    }

    [AvaloniaTheory]
    [InlineData("mono", 12)]
    [InlineData("mono small", 11.5)]
    [InlineData("mono large", 12.5)]
    public void Mono_classes_use_roboto_mono_between_eleven_and_a_half_and_twelve_and_a_half(string classes, double size)
    {
        var block = Styled(classes);

        Assert.Equal("Roboto Mono", block.FontFamily.Name);
        Assert.Equal(size, block.FontSize);
    }

    [AvaloniaTheory]
    [InlineData("icon", 20)]
    [InlineData("icon s14", 14)]
    [InlineData("icon s16", 16)]
    [InlineData("icon s18", 18)]
    [InlineData("icon s24", 24)]
    [InlineData("icon s28", 28)]
    public void Icon_classes_use_the_symbols_font_at_the_named_size(string classes, double size)
    {
        var block = Styled(classes);

        Assert.Equal("Material Symbols Rounded", block.FontFamily.Name);
        Assert.Equal(size, block.FontSize);
        Assert.Equal(size, block.LineHeight);
    }

    [AvaloniaFact]
    public void Filled_icons_use_the_filled_face()
    {
        Assert.Equal("Material Symbols Rounded Filled", Styled("icon filled").FontFamily.Name);
    }

    [AvaloniaTheory]
    [InlineData("caption", "Md3OnSurfaceVariantBrush")]
    [InlineData("timestamp", "Md3OutlineBrush")]
    [InlineData("overline", "Md3OutlineBrush")]
    [InlineData("muted", "Md3OnSurfaceVariantBrush")]
    [InlineData("subtle", "Md3OutlineBrush")]
    [InlineData("accent", "Md3PrimaryBrush")]
    [InlineData("error", "Md3ErrorBrush")]
    [InlineData("warning", "Md3WarningBrush")]
    [InlineData("success", "Md3SuccessBrush")]
    public void Role_color_classes_use_the_palette_brush(string classes, string brushKey)
    {
        var block = Styled(classes);

        Assert.True(Application.Current!.TryGetResource(brushKey, null, out var brush));
        Assert.Same(brush, block.Foreground);
    }

    [AvaloniaFact]
    public void Unclassed_text_is_body_sized_roboto_in_the_on_surface_color()
    {
        var block = new TextBlock { Text = "Sample" };
        ThemeTestSupport.HostInWindow(block);

        Assert.Equal(13, block.FontSize);
        Assert.Equal("Roboto", block.FontFamily.Name);
        Assert.True(Application.Current!.TryGetResource("Md3OnSurfaceBrush", null, out var onSurface));
        Assert.Same(onSurface, block.Foreground);
    }
}
