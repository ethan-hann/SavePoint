using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Savepoint.App.Features.Games;

/// <summary>The Games screen; its DataContext is a <see cref="GamesScreenViewModel"/>.</summary>
public partial class GamesScreenView : UserControl
{
    private GamesScreenViewModel? _screen;

    // True while the cursor is in whichever profile search shows. Hiding the box that has focus drops focus, so this is what tells the
    // other box to take it.
    private bool _searchHadFocus;

    /// <summary>Creates the view.</summary>
    public GamesScreenView()
    {
        InitializeComponent();
        Table.SizeChanged += OnTableSizeChanged;
        Root.SizeChanged += OnRootSizeChanged;
        ProfilesBody.SizeChanged += OnBodySizeChanged;
        foreach (var host in new[] { PinnedSearchHost, ScrollingSearchHost })
        {
            host.AddHandler(GotFocusEvent, OnSearchGotFocus);
            host.AddHandler(LostFocusEvent, OnSearchLostFocus);
        }

        AddHandler(KeyDownEvent, OnKeyDown);
        AddHandler(ContextRequestedEvent, OnContextRequested, RoutingStrategies.Tunnel);
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_screen is not null)
        {
            _screen.PropertyChanged -= OnScreenChanged;
        }

        _screen = DataContext as GamesScreenViewModel;
        if (_screen is not null)
        {
            _screen.PropertyChanged += OnScreenChanged;
        }

        Feed(Table.Bounds.Width);
        FeedHeader(Root.Bounds.Size);
        FeedBody();
    }

    // A row is a Tab stop that carries the row's spoken summary, so Enter on it plays the row; a button inside the row handles its own Enter first.
    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // Avalonia opens a context menu from the menu key but not from Shift+F10, which Windows and Linux players also reach for.
        if (e.Key == Key.F10 && e.KeyModifiers == KeyModifiers.Shift && TableRowOf(e.Source) is { } menuRow)
        {
            menuRow.RaiseEvent(new ContextRequestedEventArgs { RoutedEvent = ContextRequestedEvent, Source = menuRow });
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && e.Source is Border { DataContext: ProfileRowViewModel profile } row && row.Classes.Contains("table-row"))
        {
            profile.PlayCommand.Execute(null);
            e.Handled = true;
        }
    }

    private static Border? TableRowOf(object? source) =>
        (source as Visual)?.GetSelfAndVisualAncestors().OfType<Border>().FirstOrDefault(border => border.Classes.Contains("table-row"));

    // The menu's pointer placement follows the mouse, which a menu opened from the keyboard must not: it opens below the focused row instead.
    private static void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (TableRowOf(e.Source) is { ContextFlyout: MenuFlyout flyout } row)
        {
            // A mouse open asks the flyout to show at the pointer whatever its placement says, so only the keyboard case changes it.
            if (!e.TryGetPosition(row, out _))
            {
                flyout.Placement = PlacementMode.BottomEdgeAlignedLeft;
            }
        }
    }

    private void OnTableSizeChanged(object? sender, SizeChangedEventArgs e) => Feed(e.NewSize.Width);

    private void OnRootSizeChanged(object? sender, SizeChangedEventArgs e) => FeedHeader(e.NewSize);

    private void OnBodySizeChanged(object? sender, SizeChangedEventArgs e) => FeedBody();

    // A pinned launch card takes its room from this area, so the search folds into the scrolling profiles while a card leaves the table too little.
    private void FeedBody()
    {
        if (DataContext is GamesScreenViewModel screen && ProfilesBody.Bounds.Height > 0)
        {
            screen.SetBodyHeight(ProfilesBody.Bounds.Height);
        }
    }

    private void FeedHeader(Size size)
    {
        if (DataContext is GamesScreenViewModel screen && size.Width > 0)
        {
            screen.SetHeaderWidth(size.Width);
            screen.SetPaneHeight(size.Height);
        }
    }

    // A card below the table sits inside the scrolling body, so one that arrives in a short window is brought into view.
    private void OnScreenChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GamesScreenViewModel.ShowPinnedSearch) or nameof(GamesScreenViewModel.ShowScrollingSearch) && _searchHadFocus)
        {
            Dispatcher.UIThread.Post(MoveSearchFocus, DispatcherPriority.Loaded);
        }

        if (e.PropertyName == nameof(GamesScreenViewModel.InlineContent) && _screen?.HasScrollingContent == true)
        {
            Dispatcher.UIThread.Post(ScrollingContentHost.BringIntoView, DispatcherPriority.Loaded);
        }
    }

    private void OnSearchGotFocus(object? sender, RoutedEventArgs e) => _searchHadFocus = true;

    // Focus that went to another control means the player moved on; focus that went nowhere is a box that was hidden as the pane changed size.
    private void OnSearchLostFocus(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is Visual target
            && !PinnedSearchHost.IsVisualAncestorOf(target)
            && !ScrollingSearchHost.IsVisualAncestorOf(target))
        {
            _searchHadFocus = false;
        }
    }

    private void MoveSearchFocus()
    {
        if (!_searchHadFocus || _screen is null)
        {
            return;
        }

        // A second move for the same change finds the box already focused, which raises no focus event, so the flag is set here.
        _searchHadFocus = (_screen.ShowPinnedSearch ? ProfileSearchBox : ScrollingProfileSearchBox).FocusSearch();
    }

    // The table is hidden when there is nothing to list, and a hidden table measures nothing worth reacting to.
    private void Feed(double width)
    {
        if (DataContext is GamesScreenViewModel screen && width > 0)
        {
            screen.SetTableWidth(width);
        }
    }
}
