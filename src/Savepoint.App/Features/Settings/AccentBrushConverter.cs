using System.Globalization;
using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Savepoint.App.Theme;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Features.Settings;

/// <summary>
/// Turns an accent into its swatch brush: the preset's raw seed color from the <c>Md3Seed&lt;Accent&gt;Brush</c>
/// resources, which never follow the accent or mode that is applied. One way only.
/// </summary>
public sealed class AccentBrushConverter : IValueConverter
{
    /// <summary>The shared instance, for <c>{x:Static settings:AccentBrushConverter.Instance}</c>.</summary>
    public static AccentBrushConverter Instance { get; } = new();

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is AccentColorId accent
            && Application.Current is { } app
            && app.TryGetResource(ThemeKeys.Brush($"Seed{accent}"), app.ActualThemeVariant, out var brush))
        {
            return brush;
        }

        return BindingOperations.DoNothing;
    }

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Always: a brush says nothing about which accent it came from.</exception>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
