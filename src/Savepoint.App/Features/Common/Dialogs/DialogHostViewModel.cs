using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Savepoint.App.Features.Common.Dialogs;

/// <summary>
/// The modal stack behind the dialog overlay: the newest dialog is shown and the ones under it wait. The host view
/// binds <see cref="Current"/>; code that needs a dialog goes through <see cref="IDialogs"/> instead of using this.
/// </summary>
public sealed partial class DialogHostViewModel : ObservableObject
{
    /// <summary>Open dialogs, oldest first.</summary>
    public ObservableCollection<IDialogViewModel> Stack { get; } = [];

    /// <summary>The dialog on top of the stack, or null when none is open.</summary>
    public IDialogViewModel? Current => Stack.Count == 0 ? null : Stack[^1];

    /// <summary>True while any dialog is open; the window hides the page behind it from assistive technology then.</summary>
    public bool IsOpen => Stack.Count > 0;

    /// <summary>Shows a dialog on top of the stack and answers with its result once it closes.</summary>
    public Task<TResult> ShowAsync<TResult>(DialogViewModel<TResult> dialog)
    {
        if (dialog.IsClosed)
        {
            return dialog.Result;
        }

        dialog.Closed += OnDialogClosed;
        Stack.Add(dialog);
        RaiseStackChanged();
        return dialog.Result;
    }

    /// <summary>Asks the dialog on top to close the way Escape would; nothing happens when none is open or it cannot be dismissed.</summary>
    public void RequestDismissCurrent() => Current?.RequestDismiss();

    private void OnDialogClosed(object? sender, EventArgs e)
    {
        var dialog = (IDialogViewModel)sender!;
        dialog.Closed -= OnDialogClosed;
        Stack.Remove(dialog);
        RaiseStackChanged();
    }

    private void RaiseStackChanged()
    {
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(IsOpen));
    }
}
