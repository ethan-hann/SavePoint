using System.ComponentModel;

namespace Savepoint.App.Features.Common.Dialogs;

/// <summary>
/// What the dialog host needs from any dialog it shows. Implement it by deriving from <see cref="DialogViewModel{TResult}"/>;
/// the view is found by the naming convention in <see cref="ViewLocator"/> (<c>FooDialogViewModel</c> to <c>FooDialogView</c>).
/// </summary>
public interface IDialogViewModel : INotifyPropertyChanged
{
    /// <summary>The dialog's accessible name, already localized.</summary>
    string Title { get; }

    /// <summary>Whether Escape and a click on the scrim close the dialog. False while it is busy, or when the user must choose.</summary>
    bool CanDismiss { get; }

    /// <summary>
    /// What the dialog is about, in a sentence or two, exposed to assistive technology as the dialog's description. Focus lands on
    /// the first control, so without this a screen reader user hears only the title and never the message that matters most
    /// ("2 snapshots are only in this profile").
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Whether a click on the scrim closes the dialog. True only for a dialog with nothing to lose, such as a plain confirmation: a
    /// dialog that holds typed input or a decision stays open, since the scrim is the largest target on screen and the one a
    /// player clicks on the way back from a game. Escape and the dialog's own buttons still close it.
    /// </summary>
    bool DismissesOnScrimClick { get; }

    /// <summary>
    /// Whether the dialog's view arranges itself as a scrolling body above a footer that stays in view. False means the host
    /// scrolls the whole view, buttons included, which is right for a dialog that is never taller than a small window.
    /// </summary>
    bool PinsFooter => false;

    /// <summary>Width of the dialog chrome in device-independent pixels.</summary>
    double DialogWidth { get; }

    /// <summary>True once <see cref="Closed"/> has been raised.</summary>
    bool IsClosed { get; }

    /// <summary>Closes the dialog with its dismiss result when <see cref="CanDismiss"/> allows it; the host calls this for Escape and the scrim.</summary>
    void RequestDismiss();

    /// <summary>Raised once, when the dialog is closed by any route; the host removes it from the stack then.</summary>
    event EventHandler? Closed;
}
