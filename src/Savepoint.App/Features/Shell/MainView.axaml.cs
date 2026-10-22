using Avalonia.Controls;
using Savepoint.App.Features.Common.Accessibility;

namespace Savepoint.App.Features.Shell;

/// <summary>The content of the main window; its DataContext is a <see cref="MainWindowViewModel"/>.</summary>
public partial class MainView : UserControl, IAnnouncementHost
{
    /// <summary>Creates the view.</summary>
    public MainView()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    public void Announce(string message) => PageAnnouncer.Announce(message);
}
