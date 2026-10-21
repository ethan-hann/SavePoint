using Avalonia.Controls;

namespace Savepoint.App.Features.Common.Toasts;

/// <summary>The toast stack; its DataContext is a <see cref="ToastHostViewModel"/>. Place it at the bottom of the window.</summary>
public partial class ToastHostView : UserControl
{
    /// <summary>Creates the stack; bind or set the DataContext.</summary>
    public ToastHostView()
    {
        InitializeComponent();
    }
}
