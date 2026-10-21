using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.Core.Errors;

namespace Savepoint.App.Features.Editor;

public sealed partial class GameEditorViewModel
{
    /// <summary>True when "Import" is offered: only while adding a game.</summary>
    public bool CanImport => IsCreating;

    /// <summary>True when "Export" is offered: only while editing a game.</summary>
    public bool CanExport => IsEditing;

    // Reads and checks a file and fills the form with it; nothing is saved until the user saves.
    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportAsync()
    {
        var request = new OpenFilePickRequest(_text.Message("gameEditor.import.pickTitle"), [DefinitionFiles()]);
        if (await _dialogs.PickOpenFileAsync(request) is not { } path)
        {
            return;
        }

        var imported = await _games.ImportDefinitionAsync(path);
        if (!imported.Ok)
        {
            _toasts.Show(new ToastMessage(_text.Message("gameEditor.import.failed"), ToastKind.Error, Describe(imported.Error)));
            return;
        }

        // The file's id may belong to a game already in the library, so the new game gets one of its own.
        _source = imported.Value;
        _pendingId = null;
        ClearErrors();
        LoadForm(_source);
        _toasts.Show(new ToastMessage(_text.Message("gameEditor.import.success", ("name", imported.Value.Name)), ToastKind.Success));
        await RefreshSuggestionsAsync();
    }

    // The file holds the game as it was last saved, so edits not saved yet are not in it; the message says so.
    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        var saved = _saved!;
        var request = new SaveFilePickRequest(_text.Message("gameEditor.export.pickTitle", ("name", saved.Name)), $"{saved.Id}.json", [DefinitionFiles()]);
        if (await _dialogs.PickSaveFileAsync(request) is not { } path)
        {
            return;
        }

        var exported = await _games.ExportDefinitionAsync(saved.Id, path);
        if (!exported.Ok)
        {
            _toasts.Show(new ToastMessage(_text.Message("gameEditor.export.failed"), ToastKind.Error, Describe(exported.Error)));
            return;
        }

        var message = IsDirty
            ? _text.Message("gameEditor.export.successUnsaved", ("name", saved.Name))
            : _text.Message("gameEditor.export.success", ("name", saved.Name));
        _toasts.Show(new ToastMessage(message, ToastKind.Success, path));
    }

    [RelayCommand]
    private async Task BackAsync()
    {
        if (IsDirty && !await _dialogs.ConfirmAsync(DiscardRequest()))
        {
            return;
        }

        _shell.ClosePane();
    }

    private ConfirmRequest DiscardRequest() => new(
        _text.Message("gameEditor.discard.title"),
        _text.Message("gameEditor.discard.message"),
        _text.Message("gameEditor.discard.confirm"),
        Destructive: true,
        CancelLabel: _text.Message("gameEditor.discard.keep"));

    private FileTypeFilter DefinitionFiles() => new(_text.Message("gameEditor.fileType"), ["*.json"]);

    // A rejected file lists every field problem in words; any other failure reads as its own message.
    private string Describe(ErrorInfo error) =>
        IssuesOf(error) is { } issues
            ? string.Join(' ', issues.Select(issue => _text.Message(issue.MessageId)).Distinct())
            : _text.Message(error);
}
