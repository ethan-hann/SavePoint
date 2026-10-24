using Avalonia.Controls;

namespace Savepoint.App.Features.Library;

/// <summary>The adding progress and result; its DataContext is a <see cref="LibraryAddDialogViewModel"/>.</summary>
public partial class LibraryAddDialogView : UserControl
{
    /// <summary>Creates the view.</summary>
    public LibraryAddDialogView()
    {
        InitializeComponent();
    }
}
