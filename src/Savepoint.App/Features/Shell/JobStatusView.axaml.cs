using Avalonia.Controls;

namespace Savepoint.App.Features.Shell;

/// <summary>The job status pill; its DataContext is a <see cref="JobStatusViewModel"/>. It hides itself while no job runs.</summary>
public partial class JobStatusView : UserControl
{
    /// <summary>Creates the pill.</summary>
    public JobStatusView()
    {
        InitializeComponent();
    }
}
