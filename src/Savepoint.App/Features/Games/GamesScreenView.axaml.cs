using System.ComponentModel;
using Avalonia.Controls;
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
    }

    private void OnTableSizeChanged(object? sender, SizeChangedEventArgs e) => Feed(e.NewSize.Width);

    // The banner or card sits below the table inside the scrolling body, so one that arrives in a short window is brought into view.
    private void OnScreenChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GamesScreenViewModel.InlineContent) && _screen?.HasInlineContent == true)
        {
            Dispatcher.UIThread.Post(InlineContentHost.BringIntoView, DispatcherPriority.Loaded);
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
