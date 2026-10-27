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

    /// <summary>Creates the view.</summary>
    public GamesScreenView()
    {
        InitializeComponent();
        Table.SizeChanged += OnTableSizeChanged;
        Root.SizeChanged += OnRootSizeChanged;
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
        if (e.PropertyName == nameof(GamesScreenViewModel.InlineContent) && _screen?.HasScrollingContent == true)
        {
            Dispatcher.UIThread.Post(ScrollingContentHost.BringIntoView, DispatcherPriority.Loaded);
        }
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
