using Avalonia;
using Avalonia.Controls.Primitives;

namespace Savepoint.App.Features.Common;

/// <summary>
/// A uniform grid whose column count follows the width it is given: as many columns as fit at <see cref="MinItemWidth"/> each,
/// up to <see cref="MaxColumns"/>, and never fewer than one.
/// </summary>
public sealed class AdaptiveUniformGrid : UniformGrid
{
    /// <summary>Defines <see cref="MinItemWidth"/>.</summary>
    public static readonly StyledProperty<double> MinItemWidthProperty =
        AvaloniaProperty.Register<AdaptiveUniformGrid, double>(nameof(MinItemWidth), 220);

    /// <summary>Defines <see cref="MaxColumns"/>.</summary>
    public static readonly StyledProperty<int> MaxColumnsProperty =
        AvaloniaProperty.Register<AdaptiveUniformGrid, int>(nameof(MaxColumns), 3);

    /// <summary>The narrowest an item may get before the grid drops a column.</summary>
    public double MinItemWidth
    {
        get => GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    /// <summary>The most columns the grid uses, however wide it is.</summary>
    public int MaxColumns
    {
        get => GetValue(MaxColumnsProperty);
        set => SetValue(MaxColumnsProperty, value);
    }

    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(UniformGrid);

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        if (!double.IsInfinity(availableSize.Width) && MinItemWidth > 0)
        {
            var fit = (int)Math.Floor(availableSize.Width / MinItemWidth);
            var columns = Math.Clamp(fit, 1, Math.Max(1, MaxColumns));
            if (Columns != columns)
            {
                Columns = columns;
            }
        }

        return base.MeasureOverride(availableSize);
    }
}
