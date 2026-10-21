using Avalonia.Controls;

namespace Savepoint.App.Features.Editor;

/// <summary>A chip list; its DataContext is a <see cref="ChipListViewModel"/>.</summary>
public partial class ChipListView : UserControl
{
    /// <summary>Creates the view.</summary>
    public ChipListView()
    {
        InitializeComponent();
    }
}
