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
            new(text.Message(label), new RelayCommand(() => EditGameRequested?.Invoke(this, new EditGameRequest(flow.GameId))), primary);

        switch (error.MessageId)
        {
            case "error.gitOperation.checkoutFailed":
                actions.Add(TryAgain() with { IsPrimary = true });
                break;
            case "error.launch.spawnFailed":
                actions.Add(Edit("launch.failure.editGame", primary: true));
                actions.Add(TryAgain());
                break;
            case "error.path.missing":
                actions.Add(Edit("launch.failure.chooseFolder", primary: true));
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

    // What the capture after a session came to: nothing to show for a clean save, a card for a partial or failed one.
    private void ShowOutcome(GameFlow flow, SessionCompletion completion, CaptureResult? result, ErrorInfo? error)
    {
        flow.RetryBusy = false;
        var failure = error ?? WholeFailure(result);
        if (failure is not null)
        {
            ShowCaptureFailure(flow, completion, failure);
        }
        else if (result is not null && result.Folders.Any(folder => !folder.Ok))
        {
            ShowPartial(flow, completion, result);
        }
        else
        {
            SetInline(flow, null);
        }
    }

    // Every folder failed, which the service may report as a result alone.
    private static ErrorInfo? WholeFailure(CaptureResult? result) =>
        result is { Folders.Count: > 0 } && result.Folders.All(folder => !folder.Ok) ? result.Folders[0].Error : null;

    private void ShowCaptureFailure(GameFlow flow, SessionCompletion completion, ErrorInfo error)
    {
        var actions = new List<ErrorCardAction>();

        // Only a plain new point can be tried again from here; the other choices change the history in ways a bare capture would not repeat.
        if (completion is SessionCompletion.CaptureNew && flow.Session is { } session)
        {
            actions.Add(new ErrorCardAction(
                _deps.Text.Message("launch.failure.tryAgain"),
                new AsyncRelayCommand(() => RetryCaptureAsync(flow, completion, session.ProfileId, null)),
                IsPrimary: true));
        }

        actions.Add(DismissAction(flow));
        SetInline(flow, new ErrorCardViewModel(error, _deps.Text, actions));
    }

    // D13.20: half a save is never a success. The card says which half worked and retries only the other.
    private void ShowPartial(GameFlow flow, SessionCompletion completion, CaptureResult result)
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

        var roles = failed.Select(folder => folder.Role).ToList();
        var retry = new ErrorCardAction(
            text.Message("launch.partial.retry", ("role", RoleName(roles[0]))),
            new AsyncRelayCommand(() => RetryCaptureAsync(flow, completion, result.ProfileId, roles)),
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

    private static string PointNameOf(SessionCompletion completion) => completion switch
    {
        SessionCompletion.CaptureNew capture => capture.PointName,
        SessionCompletion.UpdateExisting update => update.PointName,
        SessionCompletion.ForkNewProfile fork => fork.PointName,
        SessionCompletion.MoveForward move => move.PointName,
        _ => string.Empty,
    };

    // Saves again what the session's capture did not manage, and shows the outcome the same way the first try did.
    private async Task RetryCaptureAsync(GameFlow flow, SessionCompletion completion, string profileId, IReadOnlyCollection<RepoRole>? folders)
    {
        var started = await _deps.Profiles.CaptureAsync(flow.GameId, profileId, PointNameOf(completion), folders);
        if (!started.Ok)
        {
            ShowError(started.Error);
            return;
        }

        var jobId = started.Value.JobId;
        flow.RetryBusy = true;
        ShowSaving(flow, jobId);
        RaiseActivity();

        var job = await _deps.Awaiter.WaitAsync(jobId);
        EndSaving(flow);
        ShowOutcome(flow, completion, job.Result as CaptureResult, job.Status is JobStatus.Failed or JobStatus.Cancelled ? job.Error : null);
        RaiseActivity();
    }
}
