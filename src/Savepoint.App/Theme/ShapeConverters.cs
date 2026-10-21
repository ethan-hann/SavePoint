using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace Savepoint.App.Theme;

/// <summary>
/// Converters the control templates use to derive one shape from another, so a style class that only changes a
/// control's corner radius also moves its focus ring and dashed outline.
/// </summary>
public static class ShapeConverters
{
    /// <summary>
    /// Adds the converter parameter (a number of pixels, negative to shrink) to every corner of a
    /// <see cref="CornerRadius"/>, never going below zero.
    /// </summary>
    public static IValueConverter InflateCornerRadius { get; } = new InflateCornerRadiusConverter();

    /// <summary>Halves a number; given a height it yields the radius of a pill.</summary>
    public static IValueConverter Half { get; } = new HalfConverter();

    private sealed class InflateCornerRadiusConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not CornerRadius radius)
            {
                return AvaloniaProperty.UnsetValue;
            }

            var amount = parameter is null ? 0 : System.Convert.ToDouble(parameter, CultureInfo.InvariantCulture);
            return new CornerRadius(
                Math.Max(0, radius.TopLeft + amount),
                Math.Max(0, radius.TopRight + amount),
                Math.Max(0, radius.BottomRight + amount),
                Math.Max(0, radius.BottomLeft + amount));
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    private sealed class HalfConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is double number ? number / 2 : AvaloniaProperty.UnsetValue;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
