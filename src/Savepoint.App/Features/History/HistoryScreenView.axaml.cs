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
    private const double _removedShare = 0.35;
    private const double _removedFloor = 96;
    private const double _snapshotRoom = 120;

    private bool _filterKeysWired;

    /// <summary>Creates the view; its data context is a <see cref="HistoryScreenViewModel"/>.</summary>
    public HistoryScreenView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => LimitRemovedSection();

        // A header that wraps, an explanation that opens and a footer that comes and goes all move the line between the snapshots and the section.
        BodyRegion.SizeChanged += (_, _) => LimitRemovedSection();
        RemovedScroll.SizeChanged += (_, _) => LimitRemovedSection();
    }

    // An open section takes at most a third of the window and never the room the snapshots need above it, so at the smallest supported
    // size it gives way and its rows scroll instead.
    private void LimitRemovedSection()
    {
        // The snapshots and the open section split a height that does not depend on the split, so this does not chase its own result.
        var shared = BodyRegion.Bounds.Height + (RemovedScroll.IsEffectivelyVisible ? RemovedScroll.Bounds.Height : 0);
        var limit = Math.Max(_removedFloor, Math.Min(Bounds.Height * _removedShare, shared - _snapshotRoom));

        // Whole pixels only, so sub-pixel rounding in the layout cannot keep it flipping between two values.
        if (double.IsInfinity(RemovedScroll.MaxHeight) || Math.Abs(RemovedScroll.MaxHeight - limit) >= 1)
        {
            RemovedScroll.MaxHeight = limit;
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
