using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Formatting;

namespace Savepoint.App.Features.Editor;

/// <summary>One folder the consent confirmation names.</summary>
/// <param name="Label">Which folder it is, such as "Game install folder".</param>
/// <param name="Path">The fully resolved absolute path.</param>
public sealed record ConsentFolder(string Label, string Path);

/// <summary>
/// The confirmation shown before SavePoint first manages a folder: it names each fully resolved absolute path and asks
/// for an explicit yes. It answers true only when the user confirms; Escape and Cancel answer false.
/// </summary>
public sealed partial class ConsentDialogViewModel : DialogViewModel<bool>
{
    /// <param name="folders">The folders to confirm, in the order shown; at least one.</param>
    /// <param name="text">Formats the copy.</param>
    public ConsentDialogViewModel(IReadOnlyList<ConsentFolder> folders, UiFormatter text)
        : base(text.Message("gameEditor.consent.title", ("count", folders.Count)))
    {
        Folders = folders;
        Message = text.Message("gameEditor.consent.message", ("count", folders.Count));
        ConfirmLabel = text.Message("gameEditor.consent.confirm");
        CancelLabel = text.Message("common.cancel");
    }

    /// <summary>The folders to confirm.</summary>
    public IReadOnlyList<ConsentFolder> Folders { get; }

    /// <summary>What confirming means.</summary>
    public string Message { get; }

    /// <summary>The confirm button's text.</summary>
    public string ConfirmLabel { get; }

    /// <summary>The cancel button's text.</summary>
    public string CancelLabel { get; }

    /// <inheritdoc />
    public override double DialogWidth => 520;

    /// <inheritdoc />
    public override string Description => Message;

    /// <inheritdoc />
    public override bool DismissesOnScrimClick => true;

    /// <inheritdoc />
    protected override bool DismissResult => false;

    [RelayCommand]
    private void Confirm() => Close(true);

    [RelayCommand]
    private void Cancel() => Close(false);
}
