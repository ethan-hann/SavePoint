using Avalonia.Controls;

namespace Savepoint.App.Features.Shell;

/// <summary>The content of a <see cref="CloseDuringSnapshotDialogViewModel"/>, found by the view locator.</summary>
public partial class CloseDuringSnapshotDialogView : UserControl
{
    /// <summary>Creates the view; its DataContext is the dialog view model.</summary>
    public CloseDuringSnapshotDialogView()
    {
        InitializeComponent();
    }
}
