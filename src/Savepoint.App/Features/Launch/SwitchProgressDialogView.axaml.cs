using Avalonia.Controls;

namespace Savepoint.App.Features.Launch;

/// <summary>The content of a <see cref="SwitchProgressDialogViewModel"/>, found by <see cref="Common.ViewLocator"/>.</summary>
public partial class SwitchProgressDialogView : UserControl
{
    /// <summary>Creates the view; its DataContext is the dialog view model.</summary>
    public SwitchProgressDialogView()
    {
        InitializeComponent();
    }
}
