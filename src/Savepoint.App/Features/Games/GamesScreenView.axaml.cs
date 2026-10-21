using Avalonia.Controls;

namespace Savepoint.App.Features.Games;

/// <summary>The Games screen; its DataContext is a <see cref="GamesScreenViewModel"/>.</summary>
public partial class GamesScreenView : UserControl
{
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
        Feed(Table.Bounds.Width);
    }

    private void OnTableSizeChanged(object? sender, SizeChangedEventArgs e) => Feed(e.NewSize.Width);

    // The table is hidden when there is nothing to list, and a hidden table measures nothing worth reacting to.
    private void Feed(double width)
    {
        if (DataContext is GamesScreenViewModel screen && width > 0)
        {
            screen.SetTableWidth(width);
        }
    }
}
