using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Theme;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Features.Launch;

public sealed partial class LaunchFlowViewModel
{
    // A failure to start the game: the card offers what fits the error, and always a way to put it away.
    private void ShowLaunchFailure(GameFlow flow, ErrorInfo error) =>
        SetInline(flow, new ErrorCardViewModel(error, _deps.Text, LaunchFailureActions(flow, error)));

    // A failure with no better answer than reading it: a refused completion, for example.
    private void ShowPlainFailure(GameFlow flow, ErrorInfo error) =>
        SetInline(flow, new ErrorCardViewModel(error, _deps.Text, [DismissAction(flow)]));

    private ErrorCardAction DismissAction(GameFlow flow) =>
        new(_deps.Text.Message("common.dismiss"), new RelayCommand(() => SetInline(flow, null)));

    private List<ErrorCardAction> LaunchFailureActions(GameFlow flow, ErrorInfo error)
    {
        var text = _deps.Text;
        var actions = new List<ErrorCardAction>();
        ErrorCardAction TryAgain(bool reenter = false) =>
            new(text.Message("launch.failure.tryAgain"), new AsyncRelayCommand(() => reenter ? BeginAsync(flow.GameId, flow.Target!) : RestartAsync(flow)), IsPrimary: false);
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
                actions.Add(new ErrorCardAction(text.Message("launch.failure.switchAgain"), new AsyncRelayCommand(() => SwitchAgainAsync(flow)), IsPrimary: true));
                actions.Add(new ErrorCardAction(text.Message("launch.failure.reviewChanges"), new AsyncRelayCommand(() => BeginAsync(flow.GameId, flow.Target!)), IsPrimary: false));
                break;
            case "error.launch.dirtyWorkingTree":
                actions.Add(new ErrorCardAction(text.Message("launch.failure.reviewChanges"), new AsyncRelayCommand(() => BeginAsync(flow.GameId, flow.Target!)), IsPrimary: true));
                break;
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
            ? new[] { preview.Value.Install, preview.Value.External }.OfType<FolderLaunchState>().Select(folder => folder.UnfinishedSwitchTarget).FirstOrDefault(target => target is not null)
            : null;
        await BeginAsync(flow.GameId, recorded ?? flow.Target!);
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
        else
        {
            SetInline(flow, null);
        }
    }

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
        SetInline(flow, new ErrorCardViewModel(error, _deps.Text, actions));
    }

    // D13.20: half a save is never a success. The card says which half worked and retries only the other.
    private void ShowPartial(GameFlow flow, CaptureResult result)
    {
        var text = _deps.Text;
        var failed = result.Folders.Where(folder => !folder.Ok).ToList();
        var saved = result.Folders.Where(folder => folder.Ok).ToList();
        var savedRole = saved.Count == 1 ? RoleName(saved[0].Role) : "other";
        var reason = LaunchText.Reason(text, failed[0].Error);

        var rows = result.Folders
            .OrderBy(folder => folder.Role)
            .Select(folder => new FolderOutcomeRow(
                folder.Ok ? Icons.CheckCircle : Icons.Cancel,
                text.Message(folder.Ok ? "launch.partial.rowSaved" : "launch.partial.rowFailed", ("folder", text.Message(folder.Role == RepoRole.Install ? "launch.partial.folder.install" : "launch.partial.folder.external"))),
                folder.Ok))
            .ToList();

        var retry = new ErrorCardAction(
            text.Message("launch.partial.retry", ("role", RoleName(failed[0].Role))),
            new AsyncRelayCommand(() => RetryCompletionAsync(flow)),
            IsPrimary: true);

        SetInline(flow, ErrorCardViewModel.Custom(
            text,
            Icons.Warning,
            ErrorTone.Warning,
            text.Message("launch.partial.title"),
            text.Message("launch.partial.body", ("saved", savedRole), ("reason", reason)),
            detail: failed[0].Error?.Detail,
            actions: [retry, DismissAction(flow)],
            supplement: new FolderOutcomeListViewModel(rows)));
    }

    private static string RoleName(RepoRole role) => role == RepoRole.Install ? "install" : "external";

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
