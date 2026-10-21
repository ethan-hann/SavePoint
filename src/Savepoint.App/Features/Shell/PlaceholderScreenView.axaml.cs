using Avalonia.Controls;

namespace Savepoint.App.Features.Shell;

/// <summary>The stand-in for a screen that is not registered; its DataContext is a <see cref="PlaceholderScreenViewModel"/>.</summary>
public partial class PlaceholderScreenView : UserControl
{
    /// <summary>Creates the view.</summary>
    public PlaceholderScreenView()
    {
        InitializeComponent();
    }
}
