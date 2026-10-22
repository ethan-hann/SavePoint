using Avalonia;
using Avalonia.Controls;

namespace Savepoint.App.Features.Common.Dialogs;

/// <summary>
/// A dialog's layout for a body that can be taller than the window: the content scrolls and the <see cref="Footer"/>
/// (the buttons that end the dialog) stays in view below it. The view model says so with
/// <see cref="IDialogViewModel.PinsFooter"/>, which stops the host from scrolling the whole dialog.
/// </summary>
public class DialogFrame : ContentControl
{
    /// <summary>Defines <see cref="Footer"/>.</summary>
    public static readonly StyledProperty<object?> FooterProperty =
        AvaloniaProperty.Register<DialogFrame, object?>(nameof(Footer));

    /// <summary>What stays in view below the scrolling body, usually the dialog's buttons.</summary>
    public object? Footer
    {
        get => GetValue(FooterProperty);
        set => SetValue(FooterProperty, value);
    }
}
