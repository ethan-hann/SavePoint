using Avalonia.Controls;

namespace Savepoint.App.Features.Launch;

/// <summary>The content of a <see cref="LaunchDialogViewModel"/>, found by <see cref="Common.ViewLocator"/>.</summary>
public partial class LaunchDialogView : UserControl
{
    /// <summary>Creates the view; its DataContext is the dialog view model.</summary>
    public LaunchDialogView()
    {
        InitializeComponent();
    }
}
