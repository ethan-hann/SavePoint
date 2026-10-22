using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Savepoint.App.Features.Common.Dialogs;

/// <summary>
/// A confirmation: a title, a message, Cancel and a confirm button, optionally in the destructive color and optionally
/// locked until a word is typed. Shown through <see cref="IDialogs.ConfirmAsync"/>.
/// </summary>
public sealed partial class ConfirmDialogViewModel : DialogViewModel<bool>
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _typedText = string.Empty;

    /// <param name="request">What to ask.</param>
    /// <param name="cancelLabel">The text of the cancel button when the request names none.</param>
    public ConfirmDialogViewModel(ConfirmRequest request, string cancelLabel)
        : base(request.Title)
    {
        Message = request.Message;
        ConfirmLabel = request.ConfirmLabel;
        CancelLabel = request.CancelLabel ?? cancelLabel;
        IsDestructive = request.Destructive;
        Typed = request.Typed;
    }

    /// <summary>What will happen.</summary>
    public string Message { get; }

    /// <summary>The confirm button's text.</summary>
    public string ConfirmLabel { get; }

    /// <summary>The cancel button's text.</summary>
    public string CancelLabel { get; }

    /// <summary>Whether the confirm button is drawn in the error color.</summary>
    public bool IsDestructive { get; }

    /// <summary>The typed confirmation, or null when none is required.</summary>
    public TypedConfirmation? Typed { get; }

    /// <summary>True when the dialog asks for a typed word.</summary>
    public bool RequiresTypedWord => Typed is not null;

    /// <summary>Whether the confirm button is enabled: always, unless a word is required and has not been typed yet.</summary>
    public bool CanConfirm => Typed is null || string.Equals(TypedText.Trim(), Typed.Word.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether Enter confirms. A destructive confirmation does not take Enter, except one that asks for a typed word,
    /// where pressing Enter in the field is itself deliberate.
    /// </summary>
    public bool ConfirmIsDefault => !IsDestructive || RequiresTypedWord;

    /// <inheritdoc />
    public override bool DismissesOnScrimClick => Typed is null;

    /// <inheritdoc />
    protected override bool DismissResult => false;

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        // A command's Execute runs even when CanExecute is false, so the typed word is checked here too.
        if (CanConfirm)
        {
            Close(true);
        }
    }

    [RelayCommand]
    private void Cancel() => Close(false);
}
