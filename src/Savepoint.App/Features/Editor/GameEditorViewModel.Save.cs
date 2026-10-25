using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Features.Editor;

public sealed partial class GameEditorViewModel
{
    // Folders the user confirmed in this editor, so a retry after a failed save does not ask again.
    private readonly HashSet<(RepoRole Role, string Path)> _confirmed = [];

    // Folders the Api said were never confirmed, which are asked about again whatever else is true of them.
    private readonly HashSet<RepoRole> _unconfirmed = [];

    // What the Api said about rules it left off when it added the new game; null when it said nothing or the game came from the reconnect question.
    private RulesLeftOff? _leftOff;

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

        var result = IsEditing ? await UpdateEditedAsync(definition) : await AddNewAsync(definition);
        if (result is null)
        {
            return;
        }

        if (result.Ok)
        {
            if (!IsEditing)
            {
                SayWhichRulesWereLeftOff(definition, result.Value);
            }

            if (await ApplyArtworkAsync(result.Value))
            {
                await FinishAsync(result.Value);
            }
        }
        else
        {
            ShowSaveError(result.Error, resolved);
        }
    }

    // A new game is saved without a default rule that would leave out files its saves folder already holds; the player is told which.
    // The Api says why when it added the game; after the reconnect question it only returns the game, so the default rules that are
    // missing from it are the ones left off, and they were left off because the folder holds such files.
    private void SayWhichRulesWereLeftOff(GameDefinition asked, GameDefinition saved)
    {
        var left = _leftOff ?? new RulesLeftOff([.. asked.IgnorePaths.Where(rule => RunTimeOutput.DefaultRules.Contains(rule) && !saved.IgnorePaths.Contains(rule))], SavesHoldFiles: true);
        if (left.Rules.Count > 0)
        {
            var key = left.SavesHoldFiles ? "gameEditor.ignorePaths.savesKept" : "gameEditor.ignorePaths.savesUnchecked";
            _toasts.Show(new ToastMessage(_text.Message(key, ("count", left.Rules.Count), ("rules", string.Join(", ", left.Rules))), ToastKind.Info));
        }
    }

    // Null when the user backed out of the question that a moved folder can raise: a folder with history that now points at a
    // place holding few of its files may be the wrong one, and saving it is the user's call.
    private async Task<OperationResult<GameDefinition>?> UpdateEditedAsync(GameDefinition definition)
    {
        var result = await _games.UpdateAsync(definition);
        if (result.Ok || result.Error.MessageId != "error.gameService.folderMismatch")
        {
            return result;
        }

        var confirmed = await _dialogs.ConfirmAsync(new ConfirmRequest(
            _text.Message("gameEditor.folderMismatch.title"),
            _text.Message("gameEditor.folderMismatch.message", result.Error.Args),
            _text.Message("gameEditor.folderMismatch.confirm"),
            Destructive: true));
        return confirmed ? await _games.UpdateAsync(definition, confirmFolderChange: true) : null;
    }

    // Null when the user backed out of the question that adding can raise.
    private async Task<OperationResult<GameDefinition>?> AddNewAsync(GameDefinition definition)
    {
        _leftOff = null;
        var added = await _games.AddAsync(definition);
        if (!added.Ok)
        {
            return OperationResult.Failure<GameDefinition>(added.Error);
        }

        return added.Value switch
        {
            AddGameOutcome.Added game => Remember(game),
            AddGameOutcome.HistoryFound found => await ResolveHistoryAsync(definition, found.History),
            _ => null,
        };
    }

    private OperationResult<GameDefinition> Remember(AddGameOutcome.Added added)
    {
        _leftOff = added.LeftOff;
        return OperationResult.Success(added.Game);
    }

    // Old history at these folders is a question, not an error: bring its profiles back, or start without them.
    private async Task<OperationResult<GameDefinition>?> ResolveHistoryAsync(GameDefinition definition, OrphanedHistory history)
    {
        var question = new ReconnectDialogViewModel(history, _text);
        var choice = await _dialogs.ShowAsync(question);
        return choice switch
        {
            ReconnectChoice.Reconnect => await _games.ResolveOrphanAsync(definition, new OrphanResolution.Reconnect(history.ExistingGameId, question.Recovered)),
            ReconnectChoice.StartFresh => await _games.ResolveOrphanAsync(definition, new OrphanResolution.StartFresh()),
            _ => null,
        };
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
        var asked = FoldersOf(definition)
            .Where(folder => NeedsConsent(folder.Role, folder.Raw, resolved[folder.Role]))
            .ToList();
        if (asked.Count == 0)
        {
            return true;
        }

        var folders = asked.Select(folder => new ConsentFolder(folder.Label, resolved[folder.Role])).ToList();
        if (!await _dialogs.ShowAsync(new ConsentDialogViewModel(folders, _text)))
        {
            return false;
        }

        foreach (var folder in asked)
        {
            _confirmed.Add((folder.Role, resolved[folder.Role]));
            _unconfirmed.Remove(folder.Role);
        }

        return true;
    }

    private IEnumerable<(RepoRole Role, string Raw, string Label)> FoldersOf(GameDefinition definition)
    {
        yield return (RepoRole.Install, definition.Install.Path, _text.Message("gameEditor.consent.install"));
        if (definition.External is { } saves)
        {
            yield return (RepoRole.External, saves.Path, _text.Message("gameEditor.consent.saves"));
        }
    }

    // A new game's folders are all new; an edit asks only about a folder that was pointed somewhere else or that was never confirmed.
    private bool NeedsConsent(RepoRole role, string raw, string resolved)
    {
        if (_unconfirmed.Contains(role))
        {
            return true;
        }

        if (_confirmed.Contains((role, resolved)))
        {
            return false;
        }

        if (_saved is null)
        {
            return true;
        }

        var original = role == RepoRole.Install ? _saved.Install.Path : _saved.External?.Path ?? string.Empty;
        return !string.Equals(raw.Trim(), original.Trim(), StringComparison.Ordinal)
            || _statuses.FirstOrDefault(status => status.Role == role) is { Consent.Confirmed: false };
    }

    private async Task FinishAsync(GameDefinition game)
    {
        await _shell.RefreshLibraryAsync();
        _shell.SelectGame(game.Id);
        _shell.ClosePane();
    }
}
