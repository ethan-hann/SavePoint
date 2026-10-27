using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Theme;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Features.Launch;

public sealed partial class LaunchFlowViewModel
{
    // A failure to start the game: the card offers what fits the error, and always a way to put it away. A failure of the check before
    // the start has nothing to restart, so its "Try again" runs the check again for the profile the player chose.
    private void ShowLaunchFailure(GameFlow flow, ErrorInfo error, bool beforeStart = false) =>
        SetInline(flow, new ErrorCardViewModel(error, _deps.Text, LaunchFailureActions(flow, error, beforeStart), operation: ErrorOperation.Switch));

    // A file the check found held. Nothing has been switched yet, so a file in use says that, not that a switch stopped partway.
    private void ShowHeldFile(GameFlow flow, ErrorInfo held)
    {
        if (held.MessageId != "error.gitOperation.checkoutFailed")
        {
            ShowLaunchFailure(flow, held, beforeStart: true);
            return;
        }

        var text = _deps.Text;
        var kind = ErrorPresentation.For(held.MessageId);
        SetInline(flow, ErrorCardViewModel.Custom(
            text,
            kind.Icon,
            kind.Tone,
            text.Message("launch.failure.heldTitle"),
            text.Message("launch.failure.heldBody"),
            ErrorCardViewModel.PathFor(held, text),
            held.Detail,
            LaunchFailureActions(flow, held, beforeStart: true)));
    }

    // A failure with no better answer than reading it: a refused completion, for example.
    private void ShowPlainFailure(GameFlow flow, ErrorInfo error, ErrorOperation operation = ErrorOperation.Unspecified) =>
        SetInline(flow, new ErrorCardViewModel(error, _deps.Text, [DismissAction(flow)], operation: operation));

    private ErrorCardAction DismissAction(GameFlow flow) =>
        new(_deps.Text.Message("common.dismiss"), new RelayCommand(() => SetInline(flow, null)));

    private List<ErrorCardAction> LaunchFailureActions(GameFlow flow, ErrorInfo error, bool beforeStart)
    {
        var text = _deps.Text;
        var actions = new List<ErrorCardAction>();
        ErrorCardAction TryAgain(bool reenter = false) =>
            new(text.Message("launch.failure.tryAgain"), new AsyncRelayCommand(() => reenter || beforeStart ? ReenterAsync(flow) : RestartAsync(flow)), IsPrimary: false);
        ErrorCardAction Edit(string label, bool primary) =>
            new(text.Message(label), new RelayCommand(() => EditGameRequested?.Invoke(this, flow.GameId)), primary);

        switch (error.MessageId)
        {
            case "error.gitOperation.checkoutFailed":
            case "error.gitOperation.unreadableFile":
                actions.Add(TryAgain() with { IsPrimary = true });
                break;
            case "error.launch.spawnFailed":
                actions.Add(Edit("launch.failure.editGame", primary: true));
                actions.Add(TryAgain());
                break;
            case "error.path.missing":
                actions.Add(Edit("launch.failure.chooseFolder", primary: true));
                break;
            case "error.consentMarker.missing":
            case "error.consentMarker.conflict":
            case "error.pathSafety.tier1Blocked":
            case "error.pathSafety.tier2Blocked":
                actions.Add(Edit("launch.failure.editGame", primary: true));
                break;
            case "error.launch.switchInterrupted":
                // The unfinished switch is finished by starting the target the folder was heading for, which the folder's state records;
                // reviewing the changes is the way out when that is not known, since discarding is always offered there.
                // An error that names no target is a note SavePoint could not read: nothing can finish that switch, so only the review (and its discard) is offered.
                var targetKnown = error.Args.TryGetValue("targetPoint", out var target) && target is not null;
                if (targetKnown)
                {
                    actions.Add(new ErrorCardAction(text.Message("launch.failure.switchAgain"), new AsyncRelayCommand(() => SwitchAgainAsync(flow)), IsPrimary: true));
                }

                actions.Add(new ErrorCardAction(text.Message("launch.failure.reviewChanges"), new AsyncRelayCommand(() => ReenterAsync(flow)), IsPrimary: !targetKnown));
                break;
            case "error.launch.dirtyWorkingTree":
                actions.Add(new ErrorCardAction(text.Message("launch.failure.reviewChanges"), new AsyncRelayCommand(() => ReenterAsync(flow)), IsPrimary: true));
                break;
            case "error.launch.steamNotRunning":
                // Starting the client is the only thing agreed to: the game itself is never started through Steam, which could update it first.
                actions.Add(new ErrorCardAction(
                    text.Message("launch.steam.start"),
                    new AsyncRelayCommand(() =>
                    {
                        flow.StartSteam = true;
                        return beforeStart ? ReenterAsync(flow) : RestartAsync(flow);
                    }),
                    IsPrimary: true));
                actions.Add(new ErrorCardAction(text.Message("launch.steam.notNow"), new RelayCommand(() => SetInline(flow, null))));
                return actions;
            case "error.launch.alreadyRunning":
            case "error.gameService.gameRunning":
                break;
            case "error.launch.canceled":
                actions.Add(TryAgain(reenter: true));
                break;
            default:
                actions.Add(TryAgain());
                break;
        }

        actions.Add(DismissAction(flow));
        return actions;
    }

    // Starts the target the half-switched folder was heading for; without one, the review is the way forward.
    private async Task SwitchAgainAsync(GameFlow flow)
    {
        var preview = await _deps.Launch.GetPreviewAsync(flow.GameId, flow.Target!);
        var recorded = preview.Ok
            ? preview.Value.Folders.Select(folder => folder.UnfinishedSwitchTarget).FirstOrDefault(target => target is not null)
            : null;
        await ReenterAsync(flow, recorded);
    }

    // What the capture after a session came to: nothing to show for a clean save, a card for a partial or failed one.
    private void ShowOutcome(GameFlow flow, CaptureResult? result, ErrorInfo? error)
    {
        var failure = error ?? WholeFailure(result);
        if (failure is not null)
        {
            ShowCaptureFailure(flow, failure);
        }
        else if (result is not null && result.Folders.Any(folder => !folder.Ok))
        {
            ShowPartial(flow, result);
        }
        else if (result is not null && result.Folders.Any(folder => folder.LeftAlone))
        {
            ShowLeftAlone(flow, result);
        }
        else if (result is not null && result.Folders.Any(folder => folder.KeptAsNew))
        {
            ShowKeptAsNew(flow, result);
        }
        else
        {
            SetInline(flow, null);
        }
    }

    // Nothing is lost, but a replacement that became a new snapshot must not read as the replacement the player chose.
    private void ShowKeptAsNew(GameFlow flow, CaptureResult result) =>
        SetInline(flow, ErrorCardViewModel.Custom(
            _deps.Text,
            Icons.Info,
            ErrorTone.Neutral,
            _deps.Text.Message("launch.keptAsNew.title"),
            KeptAsNewSentence(result),
            actions: [DismissAction(flow)]));

    // Not a failure and nothing is lost, but a save that skipped a folder must not look like one that saved everything. When another folder's
    // replacement also became a new snapshot, the card says that too, so neither hides the other.
    private void ShowLeftAlone(GameFlow flow, CaptureResult result)
    {
        var left = result.Folders.Where(folder => folder.LeftAlone).ToList();
        var which = left.Count == 1 ? FolderRoles.Name(left[0].Role) : "other";
        var body = _deps.Text.Message("launch.leftAlone.body", ("folder", which), ("hasMods", FolderRoles.HasMods(HasModsFolder(result))));
        SetInline(flow, ErrorCardViewModel.Custom(
            _deps.Text,
            Icons.Info,
            ErrorTone.Neutral,
            _deps.Text.Message("launch.leftAlone.title"),
            result.Folders.Any(folder => folder.KeptAsNew) ? body + " " + KeptAsNewSentence(result) : body,
            actions: [DismissAction(flow)]));
    }

    private string KeptAsNewSentence(CaptureResult result)
    {
        var kept = result.Folders.Where(folder => folder.KeptAsNew).ToList();
        return _deps.Text.Message("launch.keptAsNew.body", ("folder", kept.Count == 1 ? FolderRoles.Name(kept[0].Role) : "other"), ("hasMods", FolderRoles.HasMods(HasModsFolder(result))));
    }

    // A save reports every folder the game has, so a mods outcome means the mods have a folder of their own and the game folder's words must not claim them.
    private static bool HasModsFolder(CaptureResult result) => result.Folders.Any(folder => folder.Role == RepoRole.Mods);

    // Every folder failed, which the service may report as a result alone.
    private static ErrorInfo? WholeFailure(CaptureResult? result) =>
        result is { Folders.Count: > 0 } && result.Folders.All(folder => !folder.Ok) ? result.Folders[0].Error : null;

    private void ShowCaptureFailure(GameFlow flow, ErrorInfo error)
    {
        var actions = new List<ErrorCardAction>();

        // Trying again repeats the same choice for the same session; a folder that moved on since the session began cannot be saved by repeating it.
        if (flow.Session is not null && error.MessageId != "error.launch.sessionMoved")
        {
            actions.Add(new ErrorCardAction(
                _deps.Text.Message("launch.failure.tryAgain"),
                new AsyncRelayCommand(() => RetryCompletionAsync(flow)),
                IsPrimary: true));
        }

        actions.Add(DismissAction(flow));
        SetInline(flow, new ErrorCardViewModel(error, _deps.Text, actions, operation: ErrorOperation.Snapshot));
    }

    // D13.20: half a save is never a success. The card says which half worked and retries only the other.
    private void ShowPartial(GameFlow flow, CaptureResult result)
    {
        var text = _deps.Text;
        var failed = result.Folders.Where(folder => !folder.Ok).ToList();
        var left = result.Folders.Where(folder => folder.LeftAlone).ToList();

        // A skipped folder's latest snapshot is already current, so for the sentence about what is in the snapshot it counts with the saved ones.
        var inSnapshot = result.Folders.Where(folder => folder.Saved || folder.Skipped).OrderBy(folder => FolderRoles.Order(folder.Role)).ToList();
        failed = [.. failed.OrderBy(folder => FolderRoles.Order(folder.Role))];
        var reason = LaunchText.Reason(text, failed[0].Error, ErrorOperation.Snapshot);

        // A folder the save left alone is neither saved nor failed; when nothing was saved at all, the card must not say half was.
        var nothingSaved = inSnapshot.Count == 0 && left.Count > 0;
        var title = text.Message(nothingSaved ? "launch.partial.titleNothingSaved" : "launch.partial.title");
        var hasMods = HasModsFolder(result);
        var state = PartialSnapshotText.State(text, inSnapshot.Select(folder => folder.Role), failed.Select(folder => folder.Role), hasMods);
        var body = nothingSaved
            ? text.Message("launch.partial.bodyLeftAlone", ("state", state), ("reason", reason), ("left", left.Count == 1 ? FolderRoles.Name(left[0].Role) : "other"), ("hasMods", FolderRoles.HasMods(hasMods)))
            : text.Message("launch.partial.body", ("state", state), ("reason", reason));

        var rows = result.Folders
            .OrderBy(folder => FolderRoles.Order(folder.Role))
            .Select(folder => new FolderOutcomeRow(
                folder.Saved ? Icons.CheckCircle : folder.LeftAlone || folder.Skipped ? Icons.Info : Icons.Cancel,
                text.Message(
                    folder.Saved ? "launch.partial.rowSaved" : folder.Skipped ? "launch.partial.rowSkipped" : folder.LeftAlone ? "launch.partial.rowLeftAlone" : "launch.partial.rowFailed",
                    ("folder", text.Message(
                        folder.Role switch
                        {
                            RepoRole.Install => "launch.partial.folder.install",
                            RepoRole.Mods => "launch.partial.folder.mods",
                            _ => "launch.partial.folder.external",
                        },
                        ("hasMods", FolderRoles.HasMods(hasMods))))),
                folder.Saved,
                folder.LeftAlone || folder.Skipped))
            .ToList();

        var retry = new ErrorCardAction(
            PartialSnapshotText.RetryLabel(text, [.. failed.Select(folder => folder.Role)], hasMods),
            new AsyncRelayCommand(() => RetryCompletionAsync(flow)),
            IsPrimary: true);

        SetInline(flow, ErrorCardViewModel.Custom(
            text,
            Icons.Warning,
            ErrorTone.Warning,
            title,
            body,
            supportText: failed[0].Error?.Detail,
            actions: [retry, DismissAction(flow)],
            supplement: new FolderOutcomeListViewModel(rows)));
    }


    // Asks the launch side to repeat the player's choice for the folders that did not save. The session's own events carry the rest:
    // it goes back to saving and ends again, and the outcome is shown the same way the first try's was.
    private async Task RetryCompletionAsync(GameFlow flow)
    {
        var started = await _deps.Launch.RetryCompletionAsync(flow.GameId);
        if (!started.Ok)
        {
            ShowError(started.Error);
        }
    }
}
