using Avalonia.Controls;

namespace Savepoint.App.Features.Shell;

/// <summary>The content of a <see cref="LibraryErrorViewModel"/>, found by <see cref="Common.ViewLocator"/>.</summary>
public partial class LibraryErrorView : UserControl
{
    /// <summary>Creates the view; its DataContext is the library error view model.</summary>
    public LibraryErrorView()
    {
        InitializeComponent();
    }
}
