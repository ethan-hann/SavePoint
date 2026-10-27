using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Savepoint.App.Features.Common;

namespace Savepoint.App.Features.History;

/// <summary>The History screen's view.</summary>
public partial class HistoryScreenView : UserControl
{
    private readonly VirtualizedFocusRestore[] _focus;
    private HistoryScreenViewModel? _screen;
    private bool _filterKeysWired;

    /// <summary>Creates the view; its data context is a <see cref="HistoryScreenViewModel"/>.</summary>
    public HistoryScreenView()
    {
        InitializeComponent();
        foreach (var list in new[] { GroupList, TimelineList, SavesList, RemovedList })
        {
            VirtualizedTabNavigation.Attach(list);
        }

        _focus =
        [
            new VirtualizedFocusRestore(GroupList, key => CardEntryHolding(key)),
            new VirtualizedFocusRestore(TimelineList, key => IndexOfKey(_screen?.Timeline, key)),
            new VirtualizedFocusRestore(SavesList, key => IndexOfKey(_screen?.Saves, key)),
        ];
        CardsRegion.SizeChanged += (_, _) => FitCardColumns();
        DataContextChanged += OnScreenChanged;
    }

    private void OnScreenChanged(object? sender, EventArgs e)
    {
        if (_screen is not null)
        {
            _screen.EntriesChanging -= OnEntriesChanging;
        }

        _screen = DataContext as HistoryScreenViewModel;
        if (_screen is not null)
        {
            _screen.EntriesChanging += OnEntriesChanging;
        }

        FitCardColumns();
    }

    // Keyboard focus follows its snapshot through a search, a refresh and a change of width.
    private void OnEntriesChanging(object? sender, EventArgs e)
    {
        foreach (var focus in _focus)
        {
            focus.Capture();
        }
    }

    // The line of the card list that holds the snapshot's card: a row of cards.
    private int CardEntryHolding(string key)
    {
        var entries = _screen?.CardEntries;
        for (var index = 0; entries is not null && index < entries.Count; index++)
        {
            if (entries[index] is CardRowViewModel row && row.Cards.Any(card => card.FocusKey == key))
            {
                return index;
            }
        }

        return -1;
    }

    private static int IndexOfKey<T>(IReadOnlyList<T>? items, string key)
        where T : ListedItemViewModel
    {
        for (var index = 0; items is not null && index < items.Count; index++)
        {
            if (items[index].FocusKey == key)
            {
                return index;
            }
        }

        return -1;
    }

    // As many cards side by side as fit at 210 px each, from one to three, measured on the scroll area, which does not change
    // when its scroll bar comes and goes. The list's margins take 30 px of its width, and the cards' own margins give back none.
    private void FitCardColumns()
    {
        if (DataContext is HistoryScreenViewModel screen && CardsRegion.Bounds.Width > 0)
        {
            screen.CardColumns = (int)Math.Clamp(Math.Floor((CardsRegion.Bounds.Width - 30) / 210), 1, 3);
        }
    }

    // Each opening starts from the filter in force, whatever the arrow keys did the last time, and puts focus in the list.
    private void OnFilterOpened(object? sender, EventArgs e)
    {
        if (sender is not Flyout { Content: ListBox list } || DataContext is not HistoryScreenViewModel screen)
        {
            return;
        }

        // Tunneling, so Enter and Space reach this before the list's own handling does.
        if (!_filterKeysWired)
        {
            list.AddHandler(KeyDownEvent, OnFilterKeyDown, RoutingStrategies.Tunnel);
            _filterKeysWired = true;
        }

        list.SelectedItem = screen.SelectedFilter;
        Dispatcher.UIThread.Post(() => (list.SelectedItem is null ? list : list.ContainerFromItem(list.SelectedItem) ?? list).Focus(NavigationMethod.Directional), DispatcherPriority.Loaded);
    }

    // Arrow keys move the highlight; Enter or Space chooses it.
    private void OnFilterKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Space && sender is ListBox list)
        {
            Choose(list);
            e.Handled = true;
        }
    }

    // A click on a profile chooses it; a click on the list's edge or its scroll bar does not.
    private void OnFilterPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left && sender is ListBox list && e.Source is Visual source && source.FindAncestorOfType<ListBoxItem>() is not null)
        {
            Choose(list);
        }
    }

    private void Choose(ListBox list)
    {
        if (list.SelectedItem is ProfileFilter filter && DataContext is HistoryScreenViewModel screen)
        {
            screen.SelectedFilter = filter;
        }

        FilterButton.Flyout?.Hide();
    }
}
