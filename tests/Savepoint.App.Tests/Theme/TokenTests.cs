using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;

namespace Savepoint.App.Tests.Theme;

// The design tokens from the visual spec, read back from the application's resources.
public class TokenTests
{
    private static object Resource(string key)
    {
        var app = Application.Current ?? throw new InvalidOperationException("No application is running.");
        Assert.True(app.TryGetResource(key, null, out var value), $"No resource '{key}'.");
        return value!;
    }

    [AvaloniaTheory]
    [InlineData("Md3RadiusPill", 100)]
    [InlineData("Md3RadiusCard", 14)]
    [InlineData("Md3RadiusPanel", 12)]
    [InlineData("Md3RadiusDialog", 16)]
    [InlineData("Md3RadiusRow", 10)]
    [InlineData("Md3RadiusControl", 8)]
    [InlineData("Md3RadiusSmall", 6)]
    public void Radii_match_the_spec(string key, double radius)
    {
        Assert.Equal(new CornerRadius(radius), Resource(key));
    }

    [AvaloniaTheory]
    [InlineData("ControlCornerRadius", 8)]
    [InlineData("OverlayCornerRadius", 10)]
    public void Fluents_radius_keys_follow_the_spec_so_kept_controls_match(string key, double radius)
    {
        Assert.Equal(new CornerRadius(radius), Resource(key));
    }

    [AvaloniaTheory]
    [InlineData("Md3Space6", 6)]
    [InlineData("Md3Space9", 9)]
    [InlineData("Md3Space12", 12)]
    [InlineData("Md3Space14", 14)]
    [InlineData("Md3Space20", 20)]
    [InlineData("Md3Space26", 26)]
    public void Spacing_steps_are_named_by_pixel_value(string key, double pixels)
    {
        Assert.Equal(pixels, Resource(key));
    }

    [AvaloniaFact]
    public void Dialog_shadow_is_twelve_down_forty_blur_at_half_black()
    {
        var shadows = Assert.IsType<BoxShadows>(Resource("Md3DialogShadow"));

        Assert.Equal(1, shadows.Count);
        var shadow = shadows[0];
        Assert.Equal(0, shadow.OffsetX);
        Assert.Equal(12, shadow.OffsetY);
        Assert.Equal(40, shadow.Blur);
        Assert.Equal(0, shadow.Spread);
        Assert.Equal(Color.FromArgb(0x80, 0, 0, 0), shadow.Color);
    }

    [AvaloniaFact]
    public void Focus_outline_is_one_and_a_half_pixels_and_state_layers_follow_material()
    {
        Assert.Equal(new Thickness(1.5), Resource("Md3BorderFocus"));
        Assert.Equal(new Thickness(1), Resource("Md3BorderThin"));
        Assert.Equal(0.08, Resource("Md3OpacityHover"));
        Assert.Equal(0.12, Resource("Md3OpacityPressed"));
        Assert.Equal(0.12, Resource("Md3OpacityFocus"));
    }

    [AvaloniaFact]
    public void Font_families_for_text_mono_and_icons_are_defined()
    {
        Assert.Equal("Roboto", Assert.IsType<FontFamily>(Resource("RobotoFont")).Name);
        Assert.Equal("Roboto Mono", Assert.IsType<FontFamily>(Resource("RobotoMonoFont")).Name);
        Assert.Equal("Material Symbols Rounded", Assert.IsType<FontFamily>(Resource("SymbolsFont")).Name);
        Assert.Equal("Material Symbols Rounded Filled", Assert.IsType<FontFamily>(Resource("SymbolsFilledFont")).Name);
    }
}
