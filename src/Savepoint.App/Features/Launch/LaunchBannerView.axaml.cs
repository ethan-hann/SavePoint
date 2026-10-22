using Avalonia;
using Avalonia.Controls;
using Savepoint.App.Features.Common.Accessibility;

namespace Savepoint.App.Features.Launch;

/// <summary>The content of a <see cref="LaunchBannerViewModel"/>, found by <see cref="Common.ViewLocator"/>.</summary>
public partial class LaunchBannerView : UserControl
{
    /// <summary>Creates the view; its DataContext is the banner view model.</summary>
    public LaunchBannerView()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is LaunchBannerViewModel banner)
        {
            Announcements.Appeared(this, $"{banner.Title} {banner.Detail}".Trim());
        }
    }
}
