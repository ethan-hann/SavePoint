using Avalonia.Controls;

namespace Savepoint.App.Features.Shell;

/// <summary>The content of the main window; its DataContext is a <see cref="MainWindowViewModel"/>.</summary>
public partial class MainView : UserControl
{
    /// <summary>Creates the view.</summary>
    public MainView()
    {
        InitializeComponent();
    }
}
