using Avalonia.Controls;

namespace Savepoint.App.Features.Launch;

/// <summary>The content of a <see cref="LaunchBannerViewModel"/>, found by <see cref="Common.ViewLocator"/>.</summary>
public partial class LaunchBannerView : UserControl
{
    /// <summary>Creates the view; its DataContext is the banner view model.</summary>
    public LaunchBannerView()
    {
        InitializeComponent();
    }
}
