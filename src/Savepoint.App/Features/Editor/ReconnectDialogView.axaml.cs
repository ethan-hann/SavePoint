using Avalonia.Controls;

namespace Savepoint.App.Features.Editor;

/// <summary>The reconnect question's content; its DataContext is a <see cref="ReconnectDialogViewModel"/>.</summary>
public partial class ReconnectDialogView : UserControl
{
    /// <summary>Creates the view.</summary>
    public ReconnectDialogView()
    {
        InitializeComponent();
    }
}
