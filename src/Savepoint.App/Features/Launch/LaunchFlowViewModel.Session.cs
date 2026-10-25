using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.Core.Api;
using Savepoint.Core.Errors;
using Savepoint.Core.Launch;

namespace Savepoint.App.Features.Launch;

public sealed partial class LaunchFlowViewModel
{
    private void OnSessionChanged(object? sender, LaunchSessionInfo session) =>
        _deps.Dispatcher.Post(() =>
        {
            if (!_disposed)
            {
                var flow = FlowFor(session.GameId);
                Enqueue(flow, () => ApplyAsync(flow, session));
            }
        });

    private void OnJobChanged(object? sender, JobSnapshot job) =>
        _deps.Dispatcher.Post(() =>
        {
            if (_disposed)
            {
                return;
            }

            foreach (var flow in _flows.Values)
            {
                if (flow.SavingJobId == job.JobId && flow.Saving is { } saving && job.Revision > flow.SavingRevision)
                {
                    flow.SavingRevision = job.Revision;
                    saving.Update(job.Progress);
                }

                if (flow.SwitchJobId == job.JobId && flow.Progress is { } switching && job.Revision > flow.SwitchRevision)
                {
                    flow.SwitchRevision = job.Revision;
                    switching.Update(job.Progress);
                }
            }
        });

    // Session changes for one game are applied one at a time and in order, even when applying one has to wait on the Api.
    private void Enqueue(GameFlow flow, Func<Task> work)
    {
        flow.Work.Enqueue(work);
        if (!flow.Pumping)
        {
            flow.Pumping = true;
            _ = PumpAsync(flow);
        }
    }

    private async Task PumpAsync(GameFlow flow)
    {
        try
        {
            while (flow.Work.TryDequeue(out var work))
            {
                try
                {
                    await work();
                }
                catch (Exception error)
                {
                    // Nothing in the flow throws on purpose; a bug here must not leave the game stuck without a word.
                    ShowError(ErrorInfo.From(error));
                }
            }
        }
        finally
        {
            flow.Pumping = false;
        }
    }

    private async Task ApplyAsync(GameFlow flow, LaunchSessionInfo session)
    {
        var held = flow.Session;
        if (held is null || held.LaunchJobId != session.LaunchJobId)
        {
            // A session that is already over, and that this flow never started, has nothing left to show.
            if (IsEnded(session.State) && flow.Stage != FlowStage.Starting)
            {
                return;
            }

            CloseProgress(flow);
        }
        else if (session.Revision <= held.Revision)
        {
            return;
        }

        flow.Session = session;

        // A session adopted from before the flow existed still knows what it was started for, which "Try again" needs.
        flow.Target ??= session.Target;
        flow.Stage = IsEnded(session.State) ? FlowStage.Idle : FlowStage.Session;
        await EnsureNamesAsync(flow.GameId, session.ProfileId);

        switch (session.State)
        {
            case SessionState.Preparing:
                if (!flow.ViaBanner)
                {
                    ShowProgress(flow);
                }

                break;

            case SessionState.Switching switching:
                ShowProgress(flow).Update(switching.Progress);
                break;

            case SessionState.Running running:
                CloseProgress(flow);
                SetInline(flow, new RunningCardViewModel(
                    GameName(flow.GameId),
                    ProfileName(flow.GameId, session.ProfileId),
                    running.StartedAt,
                    _deps.Text,
                    _deps.Clock,
                    _deps.Dispatcher,
                    () => DonePlayingAsync(flow.GameId)));
                break;

            case SessionState.Exited exited:
                CloseProgress(flow);
                SetInline(flow, null);
                await HandleExitAsync(flow, session, exited);
                break;

            case SessionState.Completing completing:
                ShowSaving(flow, completing.JobId);
                break;

            case SessionState.Completed completed:
                EndSaving(flow);
                ShowOutcome(flow, completed.Result, completed.Error);
                break;

            case SessionState.Failed failed:
                CloseProgress(flow);
                SetInline(flow, null);
                ShowLaunchFailure(flow, failed.Error);
                break;
        }

        RaiseActivity();
    }

    private SwitchProgressDialogViewModel ShowProgress(GameFlow flow)
    {
        if (flow.Progress is { } open)
        {
            return open;
        }

        var dialog = new SwitchProgressDialogViewModel(
            GameName(flow.GameId),
            flow.Preview?.TargetInstall,
            _deps.Text,
            _deps.Clock,
            () => CancelJobAsync(flow.SwitchJobId ?? flow.Session!.LaunchJobId),
            flow.SwitchOnly);
        flow.Progress = dialog;

        // The dialog ends when the flow closes it, so nothing waits for its result.
        _ = _deps.Dialogs.ShowAsync(dialog);
        return dialog;
    }

    private static void CloseProgress(GameFlow flow)
    {
        flow.Progress?.Finish();
        flow.Progress = null;
    }

    private async Task HandleExitAsync(GameFlow flow, LaunchSessionInfo session, SessionState.Exited exited)
    {
        var settings = await LoadSettingsAsync();
        if (!settings.ConfirmSessionCapture)
        {
            // The dialog that would have explained a wrong program name is off, so the player hears of it once here.
            var name = GameName(flow.GameId);
            switch (exited.EndKind)
            {
                case SessionEndKind.NamedProcessNeverAppeared:
                    _deps.Toasts.Show(new ToastMessage(_deps.Text.Message("launch.postPlay.notSeen.toast", ("gameName", name)), ToastKind.Info));
                    break;
                case SessionEndKind.NamedProcessNeverMatched:
                    _deps.Toasts.Show(new ToastMessage(_deps.Text.Message("launch.postPlay.neverMatched.toast", ("gameName", name)), ToastKind.Info));
                    break;
            }

            // Nothing is captured; the dirty check before the next switch is what protects the session.
            var skipped = await _deps.Launch.CompleteSessionAsync(flow.GameId, new SessionCompletion.Skip());
            if (!skipped.Ok)
            {
                ShowPlainFailure(flow, skipped.Error, ErrorOperation.Snapshot);
            }

            return;
        }

        // The player's choice arrives when the dialog closes, outside this queue; it is applied through the queue again, in order.
        var dialog = new PostPlayDialogViewModel(exited, ProfileName(flow.GameId, session.ProfileId), _deps, !settings.OlderChoiceHelpSeen, !settings.ReplaceHelpSeen, () => EditGameRequested?.Invoke(this, flow.GameId), flow.GameId);
        dialog.Closed += (_, _) => Enqueue(flow, () => CompleteAsync(flow, dialog));
        _ = _deps.Dialogs.ShowAsync(dialog);
    }

    // The dialog is closed by now, so its result is already there.
    private async Task CompleteAsync(GameFlow flow, PostPlayDialogViewModel dialog)
    {
        var completed = await _deps.Launch.CompleteSessionAsync(flow.GameId, await dialog.Result);
        if (!completed.Ok)
        {
            ShowPlainFailure(flow, completed.Error, ErrorOperation.Snapshot);
            RaiseActivity();
        }
    }

    private void ShowSaving(GameFlow flow, Guid jobId)
    {
        var saving = new LaunchSavingViewModel(_deps.Text, () => CancelJobAsync(jobId));
        flow.Saving = saving;
        flow.SavingJobId = jobId;
        flow.SavingRevision = 0;
        _followedSaves.Add(jobId);
        SetInline(flow, saving);

        if (_deps.Jobs.Get(jobId) is { } job)
        {
            flow.SavingRevision = job.Revision;
            saving.Update(job.Progress);
        }
    }

    private static void EndSaving(GameFlow flow)
    {
        flow.Saving = null;
        flow.SavingJobId = null;
    }

    private async Task<bool> DonePlayingAsync(string gameId)
    {
        var finished = await _deps.Launch.MarkFinishedAsync(gameId);
        if (finished.Ok)
        {
            return true;
        }

        ShowError(finished.Error);
        return false;
    }

    private async Task<bool> CancelJobAsync(Guid jobId)
    {
        var cancelled = await _deps.Jobs.CancelAsync(jobId);
        if (cancelled.Ok)
        {
            return true;
        }

        ShowError(cancelled.Error);
        return false;
    }
}
