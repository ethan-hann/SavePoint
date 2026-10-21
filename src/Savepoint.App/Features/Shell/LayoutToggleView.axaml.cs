using Avalonia.Controls;

namespace Savepoint.App.Features.Shell;

/// <summary>The two-segment layout toggle; its DataContext is a <see cref="LayoutToggleViewModel"/>.</summary>
public partial class LayoutToggleView : UserControl
{
    /// <summary>Creates the toggle; set its DataContext to <see cref="IShellContext.LayoutToggle"/>.</summary>
    public LayoutToggleView()
    {
        InitializeComponent();
    }
}
