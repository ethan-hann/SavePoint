using Savepoint.App.Theme.Material;

namespace Savepoint.App.Tests.Theme.Material;

public class ColorUtilsTests
{
    [Fact]
    public void ArgbFromRgb_packs_an_opaque_color()
    {
        Assert.Equal(0xFF123456u, ColorUtils.ArgbFromRgb(0x12, 0x34, 0x56));
    }

    [Fact]
    public void ArgbFromRgb_masks_each_channel_to_a_byte()
    {
        Assert.Equal(0xFF010203u, ColorUtils.ArgbFromRgb(0x101, 0x202, 0x303));
    }

    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(255, 100.0)]
    public void Linearized_maps_the_channel_range_onto_zero_to_one_hundred(int channel, double expected)
    {
        Assert.Equal(expected, ColorUtils.Linearized(channel));
    }

    [Theory]
    [InlineData(100.0, 255)]
    [InlineData(0.0, 0)]
    [InlineData(-5.0, 0)]
    [InlineData(1000.0, 255)]
    public void Delinearized_clamps_to_a_byte(double linear, int expected)
    {
        Assert.Equal(expected, ColorUtils.Delinearized(linear));
    }

    // These inputs make delinearized * 255 land exactly on 0.5 and 2.5, where JS rounds up and banker's rounding does not.
    [Theory]
    [InlineData(0.015176349177441876, 1)]
    [InlineData(0.07588174588720938, 3)]
    public void Delinearized_rounds_an_exact_half_up(double linear, int expected)
    {
        Assert.Equal(expected, ColorUtils.Delinearized(linear));
    }

    [Fact]
    public void LstarFromArgb_gives_100_for_white_and_0_for_black()
    {
        Assert.Equal(100.0, ColorUtils.LstarFromArgb(0xFFFFFFFF), 9);
        Assert.Equal(0.0, ColorUtils.LstarFromArgb(0xFF000000), 9);
    }

    [Fact]
    public void YFromLstar_inverts_LstarFromY()
    {
        Assert.Equal(50.0, ColorUtils.LstarFromY(ColorUtils.YFromLstar(50.0)), 9);
    }

    [Fact]
    public void ArgbFromLstar_gives_a_gray_with_the_matching_lightness()
    {
        var gray = ColorUtils.ArgbFromLstar(50.0);

        Assert.Equal(0xFF777777u, gray);
    }
}
