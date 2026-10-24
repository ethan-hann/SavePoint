using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Savepoint.App.Features.Activity;

/// <summary>One job's details; its DataContext is a <see cref="JobDetailsViewModel"/>.</summary>
public partial class JobDetailsView : UserControl
{
    // A reader within this many pixels of the end counts as at the end, so a rounding gap never reads as scrolling up.
    private const double _endTolerance = 2;

    // The least height the lines keep; a smaller pane scrolls instead of squeezing them away.
    private const double _minLinesHeight = 180;

    private JobDetailsViewModel? _details;
    private ScrollViewer? _scroll;

    /// <summary>Creates the view.</summary>
    public JobDetailsView()
    {
        InitializeComponent();
        LineList.SelectionChanged += OnSelectionChanged;
        LayoutUpdated += (_, _) => FitLines();
        LineList.AddHandler(KeyDownEvent, OnLineKeyDown, RoutingStrategies.Tunnel);
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_details is not null)
        {
            _details.Lines.CollectionChanged -= OnLinesChanged;
            _details.CopyRequested -= OnCopyRequested;
        }

        _details = DataContext as JobDetailsViewModel;
        if (_details is not null)
        {
            _details.Lines.CollectionChanged += OnLinesChanged;
            _details.CopyRequested += OnCopyRequested;
            _details.IsFollowing = true;
            Dispatcher.UIThread.Post(ScrollToEnd, DispatcherPriority.Loaded);
        }
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_scroll is not null)
        {
            _scroll.ScrollChanged -= OnScrollChanged;
            _scroll = null;
        }
    }

    // New lines scroll into view only while the reader is at the end; a reader who scrolled up keeps their place.
    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_details is { IsFollowing: true } && e.Action == NotifyCollectionChangedAction.Add)
        {
            Dispatcher.UIThread.Post(ScrollToEnd, DispatcherPriority.Loaded);
        }
    }

    private void ScrollToEnd()
    {
        if (Scroll() is { } scroll)
        {
            scroll.ScrollToEnd();
        }
    }

    private ScrollViewer? Scroll()
    {
        if (_scroll is null && LineList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is { } found)
        {
            _scroll = found;
            _scroll.ScrollChanged += OnScrollChanged;
        }

        return _scroll;
    }

    // The list growing or the pane resizing changes the extent or the viewport; only the reader (or the follow) moves the offset, which decides whether to follow.
    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_details is null || _scroll is null || e.OffsetDelta == default)
        {
            return;
        }

        _details.IsFollowing = _scroll.Offset.Y >= _scroll.Extent.Height - _scroll.Viewport.Height - _endTolerance;
    }

    // The lines take the pane's height left over by everything above them, and never less than a usable amount: a short pane scrolls.
    private void FitLines()
    {
        var room = Scroller.Bounds.Height;
        if (room <= 0)
        {
            return;
        }

        var others = Root.DesiredSize.Height - LinesHost.Height;
        var height = Math.Max(_minLinesHeight, room - others);
        if (Math.Abs(LinesHost.Height - height) < 0.5)
        {
            return;
        }

        var following = _details?.IsFollowing ?? true;
        LinesHost.Height = height;
        if (following)
        {
            Dispatcher.UIThread.Post(ScrollToEnd, DispatcherPriority.Loaded);
        }
    }

    private void OnJumpToLatest(object? sender, RoutedEventArgs e)
    {
        if (_details is null)
        {
            return;
        }

        _details.IsFollowing = true;
        ScrollToEnd();
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e) =>
        _details?.SetSelection([.. LineList.SelectedItems?.OfType<JobLineViewModel>() ?? []]);

    // The copy shortcut in the list copies what is selected, the way Copy does.
    private void OnLineKeyDown(object? sender, KeyEventArgs e)
    {
        if (CopyKey.Matches(e.Key, e.KeyModifiers) && _details?.CopyCommand.CanExecute(null) == true)
        {
            _details.CopyCommand.Execute(null);
            e.Handled = true;
        }
    }

    private async void OnCopyRequested(object? sender, string text)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }
}
