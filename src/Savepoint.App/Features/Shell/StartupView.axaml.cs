using Avalonia.Controls;

namespace Savepoint.App.Features.Shell;

/// <summary>The startup screen; its DataContext is a <see cref="StartupViewModel"/>.</summary>
public partial class StartupView : UserControl
{
    /// <summary>Creates the view.</summary>
    public StartupView()
    {
        InitializeComponent();
    }
}
