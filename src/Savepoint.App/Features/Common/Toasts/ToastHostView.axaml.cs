using Avalonia;
using Avalonia.Controls;

namespace Savepoint.App.Features.Common.Toasts;

/// <summary>
/// The toast stack; its DataContext is a <see cref="ToastHostViewModel"/>. It sits in its own row below the page, so it
/// never covers anything. While the pointer or keyboard focus is on it, every timer is held.
/// </summary>
public partial class ToastHostView : UserControl
{
    /// <summary>Creates the stack; bind or set the DataContext.</summary>
    public ToastHostView()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsPointerOverProperty || change.Property == IsKeyboardFocusWithinProperty)
        {
            (DataContext as ToastHostViewModel)?.SetPaused(IsPointerOver || IsKeyboardFocusWithin);
        }
    }
}
