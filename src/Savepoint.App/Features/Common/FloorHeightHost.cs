using Avalonia;
using Avalonia.Controls;

namespace Savepoint.App.Features.Common;

/// <summary>
/// Hosts a child that fills whatever height its parent gives it but never asks for less than <see cref="Floor"/>. Inside a scroll
/// area, where the height on offer is unbounded, it reports exactly <see cref="Floor"/> to the layout, so a docked layout in that area
/// gives the child the rest of the viewport when there is room and the floor, with the screen scrolling, when there is not.
/// </summary>
public sealed class FloorHeightHost : Decorator
{
    /// <summary>Defines <see cref="Floor"/>.</summary>
    public static readonly StyledProperty<double> FloorProperty =
        AvaloniaProperty.Register<FloorHeightHost, double>(nameof(Floor), 0);

    static FloorHeightHost()
    {
        AffectsMeasure<FloorHeightHost>(FloorProperty);
    }

    /// <summary>The least height, in device-independent pixels, the child is given.</summary>
    public double Floor
    {
        get => GetValue(FloorProperty);
        set => SetValue(FloorProperty, value);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        // The child sees the floor, never the unbounded height, so a list inside it does not grow to its full length.
        Child?.Measure(new Size(availableSize.Width, Floor));
        return new Size(Child?.DesiredSize.Width ?? 0, Floor);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        Child?.Arrange(new Rect(finalSize));
        return finalSize;
    }
}
