using CommunityToolkit.Mvvm.Input;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Features.Editor;

public sealed partial class GameEditorViewModel
{
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        IsSaving = true;
        try
        {
            await SaveCoreAsync();
        }
        finally
        {
            IsSaving = false;
        }
    }

    private async Task SaveCoreAsync()
    {
        ClearErrors();
        _pendingId ??= _saved?.Id ?? CatalogIds.NewGameId(Name.Trim());
        var definition = BuildDefinition(_pendingId);

        var issues = CatalogValidator.Validate(definition);
        if (issues.Count > 0)
        {
            ShowIssues(issues);
            return;
        }

        if (await ResolveFoldersAsync(definition) is not { } resolved || !await ConfirmFoldersAsync(definition, resolved))
        {
            return;
        }

        var result = IsEditing ? await _games.UpdateAsync(definition) : await AddNewAsync(definition);
        if (result is null)
        {
            return;
        }

        if (result.Ok)
        {
            await FinishAsync(result.Value);
        }
        else
        {
            ShowSaveError(result.Error, resolved);
        }
    }

    // Null when the user backed out of the question that adding can raise.
    private async Task<OperationResult<GameDefinition>?> AddNewAsync(GameDefinition definition)
    {
        var added = await _games.AddAsync(definition);
        if (!added.Ok)
        {
            return OperationResult.Failure<GameDefinition>(added.Error);
        }

        return added.Value is AddGameOutcome.Added game ? OperationResult.Success(game.Game) : null;
    }

    // Every folder is resolved to an absolute path first: a path that cannot be resolved is a mistake in its field, and the
    // confirmation has to name the real folder.
    private async Task<Dictionary<RepoRole, string>?> ResolveFoldersAsync(GameDefinition definition)
    {
        var resolved = new Dictionary<RepoRole, string>();
        var failed = false;
        foreach (var (role, raw, _) in FoldersOf(definition))
        {
            var result = await _games.ResolvePathAsync(raw);
            if (result.Ok)
            {
                resolved[role] = result.Value;
                continue;
            }

            failed = true;
            ShowResolveFailure(role, result.Error);
        }

        return failed ? null : resolved;
    }

    // The folders a new or moved game names are confirmed before anything is saved; true when the user agreed or nothing needs it.
    private async Task<bool> ConfirmFoldersAsync(GameDefinition definition, Dictionary<RepoRole, string> resolved)
    {
        var folders = FoldersOf(definition)
            .Where(folder => NeedsConsent(folder.Role, folder.Raw))
            .Select(folder => new ConsentFolder(folder.Label, resolved[folder.Role]))
            .ToList();

        return folders.Count == 0 || await _dialogs.ShowAsync(new ConsentDialogViewModel(folders, _text));
    }

    private IEnumerable<(RepoRole Role, string Raw, string Label)> FoldersOf(GameDefinition definition)
    {
        yield return (RepoRole.Install, definition.Install.Path, _text.Message("gameEditor.consent.install"));
        if (definition.External is { } saves)
        {
            yield return (RepoRole.External, saves.Path, _text.Message("gameEditor.consent.saves"));
        }
    }

    private bool NeedsConsent(RepoRole role, string raw) => true;

    private async Task FinishAsync(GameDefinition game)
    {
        await _shell.RefreshLibraryAsync();
        _shell.SelectGame(game.Id);
        _shell.ClosePane();
    }
}
