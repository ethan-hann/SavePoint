using Avalonia;
using Avalonia.Controls;

namespace Savepoint.App.Features.Common;

/// <summary>
/// A virtualizing stack panel that keeps the entries it has drawn in the order of the list. The stock panel lists them in the order it
/// built them, which scrolling and jumping about turn upside down, and Tab, Shift+Tab and screen readers walk its children in that
/// order, so they would meet the entries out of sequence.
/// </summary>
public sealed class OrderedVirtualizingStackPanel : VirtualizingStackPanel
{
    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(VirtualizingStackPanel);

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var arranged = base.ArrangeOverride(finalSize);
        Sort();
        return arranged;
    }

    // Moves a child only when it is out of place, so a list already in order costs one pass and no change.
    private void Sort()
    {
        var wanted = GetRealizedContainers()?.OfType<Control>().Where(container => Children.Contains(container)).OrderBy(container => IndexFromContainer(container)).ToList();
        if (wanted is null)
        {
            return;
        }

        var current = Children.Where(child => wanted.Contains(child)).ToList();
        for (var at = 0; at < wanted.Count; at++)
        {
            if (!ReferenceEquals(current[at], wanted[at]))
            {
                Children.Move(Children.IndexOf(wanted[at]), Children.IndexOf(current[at]));
                current = [.. Children.Where(child => wanted.Contains(child))];
            }
        }
    }
}
