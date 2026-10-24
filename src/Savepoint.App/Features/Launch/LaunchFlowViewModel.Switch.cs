using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Theme;
using Savepoint.Core.Api;
using Savepoint.Core.Errors;

namespace Savepoint.App.Features.Launch;

public sealed partial class LaunchFlowViewModel
{
    /// <inheritdoc />
    public Task BeginSwitchAsync(string gameId, LaunchTarget target) => BeginCoreAsync(gameId, target, switchOnly: true);

    // Runs the check again for the same kind of start the card came from, so a switch never turns into a launch on the way.
    private Task ReenterAsync(GameFlow flow, LaunchTarget? target = null) =>
        BeginCoreAsync(flow.GameId, target ?? flow.Target ?? throw new InvalidOperationException("The flow has no target to start."), flow.SwitchOnly);

    private void ShowAlreadyLoaded(GameFlow flow, LaunchPreview plan) =>
        SetInline(flow, Common.Errors.ErrorCardViewModel.Custom(
            _deps.Text,
            Icons.Info,
            Common.Errors.ErrorTone.Neutral,
            _deps.Text.Message("launch.switch.alreadyTitle"),
            _deps.Text.Message("launch.switch.alreadyBody", ("profileName", ProfileName(flow.GameId, plan.ProfileId))),
            actions: [DismissAction(flow)]));

    // The switch is a job of its own with no session: the flow follows the job to its end and says how it went.
    private async Task StartSwitchAsync(GameFlow flow, LaunchTarget target)
    {
        var started = await _deps.Launch.SwitchAsync(flow.GameId, target);
        if (!started.Ok)
        {
            CloseProgress(flow);
            flow.Stage = FlowStage.Idle;
            ShowLaunchFailure(flow, started.Error);
            RaiseActivity();
            return;
        }

        var jobId = started.Value.JobId;
        flow.Stage = FlowStage.Switching;
        flow.SwitchJobId = jobId;
        flow.SwitchRevision = 0;
        var progress = ShowProgress(flow);
        if (_deps.Jobs.Get(jobId) is { } job)
        {
            flow.SwitchRevision = job.Revision;
            progress.Update(job.Progress);
        }

        RaiseActivity();
        _ = FollowSwitchAsync(flow, jobId);
    }

    private async Task FollowSwitchAsync(GameFlow flow, Guid jobId)
    {
        try
        {
            var job = await _deps.Awaiter.WaitAsync(jobId);
            if (_disposed || flow.SwitchJobId != jobId)
            {
                return;
            }

            flow.SwitchJobId = null;
            CloseProgress(flow);
            flow.Stage = FlowStage.Idle;
            if (job.Status == JobStatus.Succeeded)
            {
                SetInline(flow, null);
                var name = ProfileName(flow.GameId, flow.Preview?.ProfileId ?? job.Profile?.Id ?? string.Empty);
                _deps.Toasts.Show(new ToastMessage(_deps.Text.Message("launch.switch.done", ("profileName", name), ("gameName", GameName(flow.GameId))), ToastKind.Success));
            }
            else
            {
                ShowLaunchFailure(flow, job.Error ?? ErrorInfo.From(new InvalidOperationException("The switch ended without saying why.")));
            }

            RaiseActivity();
        }
        catch (Exception error)
        {
            // Nothing here throws on purpose; a bug must not leave the game stuck without a word.
            ShowError(ErrorInfo.From(error));
        }
    }
}
