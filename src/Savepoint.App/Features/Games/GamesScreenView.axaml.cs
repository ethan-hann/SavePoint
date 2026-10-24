using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

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
        if (e.Key == Key.Enter && e.Source is Border { DataContext: ProfileRowViewModel profile } row && row.Classes.Contains("table-row"))
        {
            profile.PlayCommand.Execute(null);
            e.Handled = true;
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
