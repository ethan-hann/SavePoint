using Avalonia.Controls;

namespace Savepoint.App.Features.History;

/// <summary>The History screen's view.</summary>
public partial class HistoryScreenView : UserControl
{
    /// <summary>Creates the view; its data context is a <see cref="HistoryScreenViewModel"/>.</summary>
    public HistoryScreenView()
    {
        InitializeComponent();
    }

    // The filter list sits in a flyout; choosing a profile closes it.
    private void OnFilterChosen(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0)
        {
            FilterButton.Flyout?.Hide();
        }
    }
}
