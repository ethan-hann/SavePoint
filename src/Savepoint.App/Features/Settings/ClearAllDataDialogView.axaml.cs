using Avalonia.Controls;

namespace Savepoint.App.Features.Settings;

/// <summary>The content of a <see cref="ClearAllDataDialogViewModel"/>, found by the view locator.</summary>
public partial class ClearAllDataDialogView : UserControl
{
    /// <summary>Creates the view; its DataContext is the dialog view model.</summary>
    public ClearAllDataDialogView()
    {
        InitializeComponent();
    }
}
