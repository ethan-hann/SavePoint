using Avalonia.Controls;

namespace Savepoint.App.Features.Library;

/// <summary>The checklist of installed games; its DataContext is a <see cref="LibraryPickerDialogViewModel"/>.</summary>
public partial class LibraryPickerDialogView : UserControl
{
    /// <summary>Creates the view.</summary>
    public LibraryPickerDialogView()
    {
        InitializeComponent();
    }
}
