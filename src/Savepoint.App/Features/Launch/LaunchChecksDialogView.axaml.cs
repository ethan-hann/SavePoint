using Avalonia.Controls;

namespace Savepoint.App.Features.Launch;

/// <summary>The content of a <see cref="LaunchChecksDialogViewModel"/>, found by <see cref="Common.ViewLocator"/>.</summary>
public partial class LaunchChecksDialogView : UserControl
{
    /// <summary>Creates the view; its DataContext is the dialog view model.</summary>
    public LaunchChecksDialogView()
    {
        InitializeComponent();
    }
}
