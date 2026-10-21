using Avalonia.Controls;

namespace Savepoint.App.Features.Launch;

/// <summary>The content of a <see cref="LaunchSavingViewModel"/>, found by <see cref="Common.ViewLocator"/>.</summary>
public partial class LaunchSavingView : UserControl
{
    /// <summary>Creates the view; its DataContext is the saving card view model.</summary>
    public LaunchSavingView()
    {
        InitializeComponent();
    }
}
