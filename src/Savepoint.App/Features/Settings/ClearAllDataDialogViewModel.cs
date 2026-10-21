using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.Core.Api;

namespace Savepoint.App.Features.Settings;

/// <summary>
/// The confirmation for removing all of SavePoint's data (D8): what goes, what stays, how much space comes back, and a
/// word the user has to type before the confirm button works. It answers true only for a confirmed removal; Escape and
/// the scrim answer false.
/// </summary>
public sealed partial class ClearAllDataDialogViewModel : DialogViewModel<bool>
{
    /// <summary>The text field's contents; the confirm button stays off until it matches <see cref="Word"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _typedText = string.Empty;

    /// <param name="preview">What the removal would take, read from the Api just before.</param>
    /// <param name="text">Formats the copy.</param>
    public ClearAllDataDialogViewModel(ClearAllDataPreview preview, UiFormatter text)
        : base(text.Message("settings.clearAll.dialog.title"))
    {
        Intro = text.Message("settings.clearAll.dialog.intro");
        Items =
        [
            text.Message("settings.clearAll.dialog.itemGames", ("games", preview.GameCount), ("profiles", preview.ProfileCount)),
            text.Message("settings.clearAll.dialog.itemStorage", ("size", text.FileSize(preview.StorageBytes)), ("path", preview.StorageRoot)),
            text.Message("settings.clearAll.dialog.itemSettings", ("path", preview.CatalogDirectory)),
        ];

        // Folders that only lose SavePoint's marker are the one thing outside SavePoint's own storage the removal touches.
        var stays = text.Message("settings.clearAll.dialog.stays");
        Stays = preview.ConsentMarkerFolders.Count == 0
            ? stays
            : $"{stays} {text.Message("settings.clearAll.dialog.markers", ("count", preview.ConsentMarkerFolders.Count))}";
        Freed = text.Message("settings.clearAll.dialog.freed", ("size", text.FileSize(preview.StorageBytes)));
        Word = text.Message("settings.clearAll.dialog.word");
        Prompt = text.Message("settings.clearAll.dialog.prompt", ("word", Word));
        ConfirmLabel = text.Message("settings.clearAll.dialog.confirm");
        CancelLabel = text.Message("common.cancel");
    }

    /// <summary>What removing everything means, in one sentence.</summary>
    public string Intro { get; }

    /// <summary>What goes: the games and profiles with their history, the storage folder and its size, SavePoint's settings.</summary>
    public IReadOnlyList<string> Items { get; }

    /// <summary>What stays: the game folders and everything in them, bar SavePoint's marker file.</summary>
    public string Stays { get; }

    /// <summary>How much space comes back ("This frees 79.3 GB.").</summary>
    public string Freed { get; }

    /// <summary>The word to type, as the catalog spells it.</summary>
    public string Word { get; }

    /// <summary>The line above the field ("Type DELETE to confirm.").</summary>
    public string Prompt { get; }

    /// <summary>The confirm button's text.</summary>
    public string ConfirmLabel { get; }

    /// <summary>The cancel button's text.</summary>
    public string CancelLabel { get; }

    /// <summary>True when the word has been typed, ignoring case and surrounding spaces.</summary>
    public bool CanConfirm => string.Equals(TypedText.Trim(), Word.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public override double DialogWidth => 520;

    /// <inheritdoc />
    protected override bool DismissResult => false;

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        // A command's Execute runs even when CanExecute is false, so the word is checked here too.
        if (CanConfirm)
        {
            Close(true);
        }
    }

    [RelayCommand]
    private void Cancel() => Close(false);
}
