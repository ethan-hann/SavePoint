using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Savepoint.App.Features.History;

/// <summary>The History screen's view.</summary>
public partial class HistoryScreenView : UserControl
{
    private bool _filterKeysWired;

    /// <summary>Creates the view; its data context is a <see cref="HistoryScreenViewModel"/>.</summary>
    public HistoryScreenView()
    {
        InitializeComponent();
        SizeChanged += (_, e) => LimitRemovedSection(e.NewSize.Height);
    }

    // An open section never takes more than a third of the window, so the snapshots above it keep room at the smallest supported size; its rows scroll instead.
    private void LimitRemovedSection(double windowHeight) =>
        RemovedScroll.MaxHeight = Math.Max(96, windowHeight * 0.35);

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
