using Savepoint.App.Theme.Material;

namespace Savepoint.App.Tests.Theme.Material;

public class DynamicColorToneTests
{
    [Theory]
    [InlineData(0.0, true)]
    [InlineData(59.4, true)]
    [InlineData(59.5, false)]
    [InlineData(60.0, false)]
    [InlineData(100.0, false)]
    public void TonePrefersLightForeground_is_true_below_tone_60(double tone, bool expected)
    {
        Assert.Equal(expected, DynamicColor.TonePrefersLightForeground(tone));
    }

    [Theory]
    [InlineData(0.0, true)]
    [InlineData(49.0, true)]
    [InlineData(49.4, true)]
    [InlineData(49.5, false)]
    [InlineData(50.0, false)]
    public void ToneAllowsLightForeground_is_true_up_to_tone_49(double tone, bool expected)
    {
        Assert.Equal(expected, DynamicColor.ToneAllowsLightForeground(tone));
    }

    [Theory]
    [InlineData(20.0, 20.0)]
    [InlineData(49.0, 49.0)]
    [InlineData(55.0, 49.0)]
    [InlineData(59.4, 49.0)]
    [InlineData(60.0, 60.0)]
    [InlineData(90.0, 90.0)]
    public void EnableLightForeground_pulls_tones_in_the_awkward_zone_down_to_49(double tone, double expected)
    {
        Assert.Equal(expected, DynamicColor.EnableLightForeground(tone));
    }

    [Fact]
    public void ForegroundTone_prefers_lighter_on_a_dark_background_and_darker_on_a_light_one()
    {
        Assert.True(DynamicColor.ForegroundTone(10.0, 4.5) > 10.0);
        Assert.True(DynamicColor.ForegroundTone(90.0, 4.5) < 90.0);
    }

    [Fact]
    public void A_background_without_a_contrast_curve_is_rejected()
    {
        var palette = TonalPalette.FromHueAndChroma(250.0, 36.0);
        var surface = DynamicColor.FromPalette("surface", _ => palette, _ => 98.0, isBackground: true);

        Assert.Throws<ArgumentException>(() =>
            DynamicColor.FromPalette("on", _ => palette, _ => 10.0, background: _ => surface));
    }

    [Fact]
    public void A_contrast_curve_without_a_background_is_rejected()
    {
        var palette = TonalPalette.FromHueAndChroma(250.0, 36.0);

        Assert.Throws<ArgumentException>(() =>
            DynamicColor.FromPalette("on", _ => palette, _ => 10.0, contrastCurve: new ContrastCurve(3, 3, 4.5, 7)));
    }

    [Fact]
    public void A_second_background_without_a_first_is_rejected()
    {
        var palette = TonalPalette.FromHueAndChroma(250.0, 36.0);
        var surface = DynamicColor.FromPalette("surface", _ => palette, _ => 98.0, isBackground: true);

        Assert.Throws<ArgumentException>(() =>
            DynamicColor.FromPalette("on", _ => palette, _ => 10.0, secondBackground: _ => surface));
    }
}
