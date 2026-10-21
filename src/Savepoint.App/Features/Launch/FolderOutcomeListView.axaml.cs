using Avalonia.Controls;

namespace Savepoint.App.Features.Launch;

/// <summary>The content of a <see cref="FolderOutcomeListViewModel"/>, found by <see cref="Common.ViewLocator"/>.</summary>
public partial class FolderOutcomeListView : UserControl
{
    /// <summary>Creates the view; its DataContext is the status list view model.</summary>
    public FolderOutcomeListView()
    {
        InitializeComponent();
    }
}
