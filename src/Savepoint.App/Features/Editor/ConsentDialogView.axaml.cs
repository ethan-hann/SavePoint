using Avalonia.Controls;

namespace Savepoint.App.Features.Editor;

/// <summary>The folder confirmation's content; its DataContext is a <see cref="ConsentDialogViewModel"/>.</summary>
public partial class ConsentDialogView : UserControl
{
    /// <summary>Creates the view.</summary>
    public ConsentDialogView()
    {
        InitializeComponent();
    }
}
