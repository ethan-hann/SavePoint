using Avalonia.Controls;

namespace Savepoint.App.Features.Library;

/// <summary>The confirmation's content; its DataContext is a <see cref="LibraryConfirmDialogViewModel"/>.</summary>
public partial class LibraryConfirmDialogView : UserControl
{
    /// <summary>Creates the view.</summary>
    public LibraryConfirmDialogView()
    {
        InitializeComponent();
    }
}
