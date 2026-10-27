using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Savepoint.App.Features.Common;

/// <summary>
/// Keeps Tab and Shift+Tab walking through every entry of a list that draws only the entries near the screen. Left alone, Tab would
/// find no stop past the last entry that happens to be drawn and leave the list. This builds the next entry, with the controls inside
/// it, before the key is handled; the list's panel is an <see cref="OrderedVirtualizingStackPanel"/> so the entries are in order.
/// </summary>
public static class VirtualizedTabNavigation
{
    // Entries with nothing to stop on (a group's heading, a row of loaded cards) are walked over; this bounds the walk.
    private const int _maxEntriesToWalk = 50;

    /// <summary>Makes Tab and Shift+Tab inside <paramref name="list"/> step through all of its entries, drawn or not.</summary>
    /// <param name="list">A list whose items panel is virtualizing.</param>
    public static void Attach(ItemsControl list) =>
        list.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Tab || e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Alt) || e.KeyModifiers.HasFlag(KeyModifiers.Meta))
        {
            return;
        }

        if (sender is not ItemsControl list || e.Source is not InputElement from)
        {
            return;
        }

        var index = EntryIndex(list, from);
        if (index < 0)
        {
            return;
        }

        // An entry drawn just now has not been rendered, which the usual handling needs before it will stop on it.
        var forward = !e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (BuildNeighbour(list, from, index, forward) is { } built)
        {
            built.Focus(NavigationMethod.Tab, e.KeyModifiers);
            e.Handled = true;
        }
    }

    // Builds the entry the key is about to move into and answers its first stop, or answers null when the next stop is in an entry
    // that was drawn already, in the entry the key is in, or beyond the list.
    private static InputElement? BuildNeighbour(ItemsControl list, InputElement from, int index, bool forward)
    {
        var inEntry = Stops(list.ContainerFromIndex(index)).ToList();
        var at = inEntry.IndexOf(from);
        if (at >= 0 && at + (forward ? 1 : -1) >= 0 && at + (forward ? 1 : -1) < inEntry.Count)
        {
            return null;
        }

        var built = false;
        for (var step = 1; step <= _maxEntriesToWalk; step++)
        {
            var next = index + (forward ? step : -step);
            if (next < 0 || next >= list.ItemCount)
            {
                return null;
            }

            var container = list.ContainerFromIndex(next);
            if (container is null)
            {
                list.ScrollIntoView(next);
                list.UpdateLayout();
                container = list.ContainerFromIndex(next);
                built = true;
            }

            var stops = Stops(container).ToList();
            if (stops.Count > 0)
            {
                return built ? (forward ? stops[0] : stops[^1]) : null;
            }
        }

        return null;
    }

    // The controls inside an entry that Tab stops on, in the order it visits them.
    internal static IEnumerable<InputElement> Stops(Control? container) =>
        container?.GetVisualDescendants().OfType<InputElement>().Where(element => element.Focusable && element.IsTabStop && element.IsEffectivelyVisible && element.IsEffectivelyEnabled) ?? [];

    // The index of the entry that holds `from`, or -1 when `from` is not inside any entry.
    internal static int EntryIndex(ItemsControl list, Visual from)
    {
        for (var visual = from; visual is not null && !ReferenceEquals(visual, list); visual = visual.GetVisualParent())
        {
            if (visual is Control control && ReferenceEquals(control.GetVisualParent(), list.ItemsPanelRoot))
            {
                return list.IndexFromContainer(control);
            }
        }

        return -1;
    }
}
