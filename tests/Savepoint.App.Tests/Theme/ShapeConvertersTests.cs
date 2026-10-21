using System.Globalization;
using Avalonia;
using Savepoint.App.Theme;

namespace Savepoint.App.Tests.Theme;

public class ShapeConvertersTests
{
    private static readonly CultureInfo _culture = CultureInfo.InvariantCulture;

    [Fact]
    public void Inflate_adds_the_parameter_to_every_corner()
    {
        var result = ShapeConverters.InflateCornerRadius.Convert(new CornerRadius(8, 6, 4, 2), typeof(CornerRadius), 4, _culture);

        Assert.Equal(new CornerRadius(12, 10, 8, 6), result);
    }

    [Fact]
    public void Inflate_accepts_the_parameter_as_text_the_way_xaml_passes_it()
    {
        var result = ShapeConverters.InflateCornerRadius.Convert(new CornerRadius(10), typeof(CornerRadius), "-2", _culture);

        Assert.Equal(new CornerRadius(8), result);
    }

    [Fact]
    public void Inflate_never_goes_below_zero()
    {
        var result = ShapeConverters.InflateCornerRadius.Convert(new CornerRadius(1), typeof(CornerRadius), -5, _culture);

        Assert.Equal(new CornerRadius(0), result);
    }

    [Fact]
    public void Inflate_without_a_parameter_leaves_the_radius_alone()
    {
        var result = ShapeConverters.InflateCornerRadius.Convert(new CornerRadius(8), typeof(CornerRadius), null, _culture);

        Assert.Equal(new CornerRadius(8), result);
    }

    [Fact]
    public void Inflate_ignores_a_value_that_is_not_a_corner_radius()
    {
        var result = ShapeConverters.InflateCornerRadius.Convert("oops", typeof(CornerRadius), 4, _culture);

        Assert.Same(AvaloniaProperty.UnsetValue, result);
    }

    [Fact]
    public void Half_halves_a_number()
    {
        Assert.Equal(14.5, ShapeConverters.Half.Convert(29.0, typeof(double), null, _culture));
    }

    [Fact]
    public void Half_ignores_a_value_that_is_not_a_number()
    {
        Assert.Same(AvaloniaProperty.UnsetValue, ShapeConverters.Half.Convert("29", typeof(double), null, _culture));
    }

    [Fact]
    public void Neither_converter_converts_back()
    {
        Assert.Throws<NotSupportedException>(() => ShapeConverters.Half.ConvertBack(1.0, typeof(double), null, _culture));
        Assert.Throws<NotSupportedException>(() => ShapeConverters.InflateCornerRadius.ConvertBack(new CornerRadius(1), typeof(CornerRadius), null, _culture));
    }
}
