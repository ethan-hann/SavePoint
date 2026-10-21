using Avalonia.Controls;

namespace Savepoint.App.Features.Common.Dialogs;

/// <summary>The content of a <see cref="ConfirmDialogViewModel"/>, found by <see cref="ViewLocator"/>.</summary>
public partial class ConfirmDialogView : UserControl
{
    /// <summary>Creates the view; its DataContext is the dialog view model.</summary>
    public ConfirmDialogView()
    {
        InitializeComponent();
    }
}
