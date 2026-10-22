using Avalonia.Controls;

namespace Savepoint.App.Features.History;

/// <summary>The content of a <see cref="BringBackDialogViewModel"/>, found by <see cref="Common.ViewLocator"/>.</summary>
public partial class BringBackDialogView : UserControl
{
    /// <summary>Creates the view; its DataContext is the dialog view model.</summary>
    public BringBackDialogView()
    {
        InitializeComponent();
    }
}
