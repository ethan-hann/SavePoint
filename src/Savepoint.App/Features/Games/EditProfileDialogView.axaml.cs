using Avalonia.Controls;

namespace Savepoint.App.Features.Games;

/// <summary>The content of an <see cref="EditProfileDialogViewModel"/>, found by <see cref="Common.ViewLocator"/>.</summary>
public partial class EditProfileDialogView : UserControl
{
    /// <summary>Creates the view; its DataContext is the dialog view model.</summary>
    public EditProfileDialogView()
    {
        InitializeComponent();
    }
}
