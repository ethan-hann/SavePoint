using Avalonia.Controls;

namespace Savepoint.App.Features.Games;

/// <summary>The "What will be captured" panel; its DataContext is a <see cref="CapturePreviewViewModel"/>.</summary>
public partial class CapturePreviewView : UserControl
{
    /// <summary>Creates the view.</summary>
    public CapturePreviewView()
    {
        InitializeComponent();
    }
}
