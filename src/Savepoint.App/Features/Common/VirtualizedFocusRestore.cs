using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace Savepoint.App.Features.Common;

/// <summary>
/// Puts keyboard focus back where it was when a virtualized list is rebuilt under it. A search or a refresh replaces the entries and
/// the controls drawn for them, which drops the focus; this notes which snapshot's control had it before the change and, once the
/// list has settled, scrolls that snapshot into view and focuses the same control in it.
/// </summary>
public sealed class VirtualizedFocusRestore
{
    private readonly ItemsControl _list;
    private readonly Func<string, int> _indexOf;
    private (string Key, int Ordinal)? _pending;

    /// <param name="list">The list to watch.</param>
    /// <param name="indexOf">The list index of the entry that holds the snapshot with the given focus key, or -1 when it is gone.</param>
    public VirtualizedFocusRestore(ItemsControl list, Func<string, int> indexOf)
    {
        _list = list;
        _indexOf = indexOf;
    }

    /// <summary>Notes the focused control when it is inside the list; call it just before the list changes.</summary>
    public void Capture()
    {
        if (TopLevel.GetTopLevel(_list)?.FocusManager?.GetFocusedElement() is not InputElement focused)
        {
            return;
        }

        var index = VirtualizedTabNavigation.EntryIndex(_list, focused);
        if (index < 0 || focused.DataContext is not IFocusKeyed { FocusKey.Length: > 0 } item)
        {
            return;
        }

        var key = item.FocusKey;
        var ordinal = VirtualizedTabNavigation.Stops(_list.ContainerFromIndex(index)).Where(stop => Holds(stop, key)).ToList().IndexOf(focused);
        _pending = (key, Math.Max(0, ordinal));
        Dispatcher.UIThread.Post(Restore, DispatcherPriority.Loaded);
    }

    private void Restore()
    {
        if (_pending is not { } pending)
        {
            return;
        }

        _pending = null;
        if (TopLevel.GetTopLevel(_list)?.FocusManager?.GetFocusedElement() is InputElement and not TopLevel and var focused
            && (VirtualizedTabNavigation.EntryIndex(_list, focused) < 0 || Holds(focused, pending.Key)))
        {
            // Focus survived the change on the same snapshot, or sits outside the list because the person moved it on.
            return;
        }

        var index = _indexOf(pending.Key);
        if (index < 0)
        {
            return;
        }

        _list.ScrollIntoView(index);
        _list.UpdateLayout();
        var stops = VirtualizedTabNavigation.Stops(_list.ContainerFromIndex(index)).Where(stop => Holds(stop, pending.Key)).ToList();
        stops.ElementAtOrDefault(Math.Min(pending.Ordinal, stops.Count - 1))?.Focus(NavigationMethod.Tab);
    }

    private static bool Holds(InputElement stop, string key) => stop.DataContext is IFocusKeyed { } item && item.FocusKey == key;
}
