using Avalonia.Controls;

namespace Savepoint.App.Features.Games;

/// <summary>The content of a <see cref="DuplicateProfileDialogViewModel"/>, found by <see cref="Common.ViewLocator"/>.</summary>
public partial class DuplicateProfileDialogView : UserControl
{
    /// <summary>Creates the view; its DataContext is the dialog view model.</summary>
    public DuplicateProfileDialogView()
    {
        InitializeComponent();
    }
}
