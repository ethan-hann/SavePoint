using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Theme;
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
                SyncSteamWait(flow, job);
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
            flow.SteamWait = null;
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

                SyncSteamWait(flow, _deps.Jobs.Get(session.LaunchJobId));
                break;

            case SessionState.Switching switching:
                EndSteamWait(flow);
                ShowProgress(flow).Update(switching.Progress);
                break;

            case SessionState.Running running:
                CloseProgress(flow);

                // A change within the same running session only updates the notice; a new card would restart its timer.
                if (flow.Inline is RunningCardViewModel current && held?.LaunchJobId == session.LaunchJobId)
                {
                    current.SetProgramExit(running.ProgramExitCode);
                    break;
                }

                var card = new RunningCardViewModel(
                    GameName(flow.GameId),
                    ProfileName(flow.GameId, session.ProfileId),
                    running.StartedAt,
                    _deps.Text,
                    _deps.Clock,
                    _deps.Dispatcher,
                    () => DonePlayingAsync(flow.GameId));
                card.SetProgramExit(running.ProgramExitCode);
                SetInline(flow, card);
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

    // While the service waits for the Steam client it started, nothing is being switched, so the switching dialog (and the banner that says
    // the game is starting now) would both be wrong. A card says what is happening and offers to stop waiting.
    private void SyncSteamWait(GameFlow flow, JobSnapshot? job)
    {
        if (flow.Session is not { State: SessionState.Preparing } session
            || job is not { Status: JobStatus.Running }
            || job.JobId != session.LaunchJobId)
        {
            return;
        }

        var steps = job.RecentLines.OfType<JobLine.Step>().Select(step => step.MessageId).ToList();
        if (!steps.Contains(JobSteps.LaunchWaitingForSteam))
        {
            return;
        }

        // Steam is signed in and the folders are read next, which can take a while: the wait is over, and what the player saw before it comes back.
        if (steps.Contains(JobSteps.LaunchSteamReady))
        {
            if (flow.SteamWait is not null)
            {
                EndSteamWait(flow);
                if (flow.ViaBanner && flow.Preview is { } plan)
                {
                    SetInline(flow, new LaunchBannerViewModel(plan, GameName(flow.GameId), ProfileName(flow.GameId, plan.ProfileId), _deps.Text, _deps.Dialogs));
                }
                else if (!flow.ViaBanner)
                {
                    ShowProgress(flow);
                }
            }

            return;
        }

        if (flow.SteamWait is not null)
        {
            return;
        }

        CloseProgress(flow);
        var text = _deps.Text;
        var card = ErrorCardViewModel.Custom(
            text,
            Icons.SportsEsports,
            ErrorTone.Neutral,
            text.Message("launch.steam.waitingTitle"),
            text.Message("launch.steam.waitingBody", ("gameName", GameName(flow.GameId))),
            actions: [new ErrorCardAction(text.Message("launch.steam.stopWaiting"), new AsyncRelayCommand(async () => await CancelJobAsync(job.JobId)), IsPrimary: false)]);
        flow.SteamWait = card;
        SetInline(flow, card);
    }

    private void EndSteamWait(GameFlow flow)
    {
        if (flow.SteamWait is { } card)
        {
            flow.SteamWait = null;
            if (ReferenceEquals(flow.Inline, card))
            {
                SetInline(flow, null);
            }
        }
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
                case SessionEndKind.ProgramFailedAtStart:
                    _deps.Toasts.Show(new ToastMessage(_deps.Text.Message("launch.postPlay.failedAtStart.toast", ("gameName", name), ("code", PostPlayDialogViewModel.ExitCodeText(exited))), ToastKind.Info));
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

        // Steam or a launcher may have taken the focus as the game quit, which would hide the dialog.
        _deps.Attention.SessionEnded(GameName(flow.GameId));

        // The player's choice arrives when the dialog closes, outside this queue; it is applied through the queue again, in order.
        var dialog = new PostPlayDialogViewModel(exited, ProfileName(flow.GameId, session.ProfileId), _deps, !settings.OlderChoiceHelpSeen, !settings.ReplaceHelpSeen, () => EditGameRequested?.Invoke(this, flow.GameId), flow.GameId);
        dialog.Closed += (_, _) => Enqueue(flow, () => CompleteAsync(flow, dialog));
        _ = _deps.Dialogs.ShowAsync(dialog);
    }

    // The dialog is closed by now, so its result is already there.
    private async Task CompleteAsync(GameFlow flow, PostPlayDialogViewModel dialog)
    {
        var completion = await dialog.Result;

        // A save waits a few seconds first, to be sure a store did not start the game again; without a card the screen would show nothing.
        // Skipping is never held up, so it needs none.
        if (completion is not SessionCompletion.Skip)
        {
            var text = _deps.Text;
            SetInline(flow, ErrorCardViewModel.Custom(text, Icons.Info, ErrorTone.Neutral, text.Message("launch.checkingClosed.title"), text.Message("launch.checkingClosed.body"), actions: []));
        }

        var completed = await _deps.Launch.CompleteSessionAsync(flow.GameId, completion);
        if (!completed.Ok)
        {
            if (completed.Error.MessageId == "error.launch.sessionStillRunning")
            {
                // The session is still over and no event will announce it again, so the way back to the choices has to be on the card.
                SetInline(flow, new ErrorCardViewModel(completed.Error, _deps.Text, [ChooseAgainAction(flow), DismissAction(flow)], operation: ErrorOperation.Snapshot));
            }
            else
            {
                ShowPlainFailure(flow, completed.Error, ErrorOperation.Snapshot);
            }

            RaiseActivity();
        }
    }

    private ErrorCardAction ChooseAgainAction(GameFlow flow) =>
        new(
            _deps.Text.Message("launch.postPlay.chooseAgain"),
            new RelayCommand(() =>
            {
                SetInline(flow, null);
                if (flow.Session is { State: SessionState.Exited exited } session)
                {
                    Enqueue(flow, () => HandleExitAsync(flow, session, exited));
                }
            }),
            IsPrimary: true);

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
