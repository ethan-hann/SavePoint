using Avalonia.Controls;

namespace Savepoint.App.Features.Games;

/// <summary>The status list inside the half-saved card; its DataContext is a <see cref="SaveOutcomeListViewModel"/>.</summary>
public partial class SaveOutcomeListView : UserControl
{
    /// <summary>Creates the view.</summary>
    public SaveOutcomeListView()
    {
        InitializeComponent();
    }
}
