using Avalonia.Controls;

namespace Savepoint.App.Features.Launch;

/// <summary>The content of a <see cref="LaunchCheckingViewModel"/>, found by <see cref="Common.ViewLocator"/>.</summary>
public partial class LaunchCheckingView : UserControl
{
    /// <summary>Creates the view; its DataContext is the checking card view model.</summary>
    public LaunchCheckingView()
    {
        InitializeComponent();
    }
}
