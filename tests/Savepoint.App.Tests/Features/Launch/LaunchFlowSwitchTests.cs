using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Features.Launch;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.Launch;

// "Switch to this profile": the launch flow's check, dialog and guards, then a switch job with no game, no session and no after-play prompt.
public class LaunchFlowSwitchTests : IDisposable
{
    private const string _file = @"C:\Games\SkyrimSE\Data\Skyrim.esm";

    private readonly FlowRig _f = new();

    public void Dispose() => _f.Dispose();

    private IReadOnlyList<ApiCall> Switches() => _f.Backend.Calls.To<ILaunchApi>(nameof(ILaunchApi.SwitchAsync));

    // The progress-dialog path: only the saves differ, and the player asked not to be asked about that.
    private async Task SwitchWithProgressAsync()
    {
        FakeAssert.Ok(await _f.Backend.Settings.SaveAsync(MockupScenario.Settings with { SkipConfirmWhenOnlySaveChanges = true }));
        await _f.Flow.BeginSwitchAsync(FlowRig.Skyrim, LaunchRig.ClearedBlackreachPoint);
    }

    [Fact]
    public async Task A_switch_starts_a_switch_job_and_not_a_launch_and_shows_progress_worded_for_a_switch()
    {
        await SwitchWithProgressAsync();

        Assert.Empty(_f.Starts());
        var call = Assert.Single(Switches());
        Assert.Equal([FlowRig.Skyrim, LaunchRig.ClearedBlackreachPoint], call.Args);
        var job = _f.Backend.Jobs.LastStarted(JobKind.Switch);
        Assert.Equal(JobStatus.Running, job.Status);
        Assert.True(_f.Flow.IsActive(FlowRig.Skyrim));
        Assert.Null(_f.Backend.Launch.GetSession(FlowRig.Skyrim));

        var progress = _f.Progress;
        Assert.Equal("Switching Skyrim Special Edition's files…", progress.Title);
        _f.Backend.Jobs.ReportProgress(job.JobId, MockupScenario.SwitchProgress);
        Assert.Contains("3.8 GB of 6.1 GB", progress.ProgressText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_finished_switch_closes_the_progress_frees_the_game_and_offers_no_after_play_prompt()
    {
        await SwitchWithProgressAsync();
        var progress = _f.Progress;

        _f.Backend.Jobs.Complete(_f.Backend.Jobs.LastStarted(JobKind.Switch).JobId);

        Assert.True(progress.IsClosed);
        Assert.False(_f.Flow.IsActive(FlowRig.Skyrim));
        Assert.Null(_f.Flow.InlineContentFor(FlowRig.Skyrim));
        Assert.Empty(_f.Dialogs.Shown.OfType<PostPlayDialogViewModel>());
        Assert.Null(_f.Backend.Launch.GetSession(FlowRig.Skyrim));
        var toast = Assert.Single(_f.Rig.Toasts.Toasts);
        Assert.Equal(ToastKind.Success, toast.Message.Kind);
        Assert.Equal("Modded playthrough - 7-2-26 is loaded. Skyrim Special Edition was not started.", toast.Message.Text);
    }

    [Fact]
    public async Task The_dialog_for_a_switch_says_the_game_does_not_start_and_confirming_it_switches_the_chosen_target()
    {
        var begin = _f.Flow.BeginSwitchAsync(FlowRig.Skyrim, LaunchRig.SurvivalTip);
        var dialog = _f.LaunchDialog;
        _f.Dialogs.AnswerConfirm(true);
        await dialog.UnsavedBlocks[0].DiscardCommand.ExecuteAsync(null);

        Assert.Equal("Switch", dialog.ConfirmLabel);
        Assert.Equal("Here's exactly what SavePoint will change. The game won't start.", dialog.Subtitle);
        dialog.SwitchCommand.Execute(null);
        await begin;

        Assert.Empty(_f.Starts());
        Assert.Single(Switches());
        Assert.Equal("Switching Skyrim Special Edition's files…", _f.Progress.Title);
    }

    [Fact]
    public async Task A_play_dialog_still_says_switch_and_play()
    {
        var dialog = await _f.Rig.DialogAsync();

        Assert.Equal("Switch and play", dialog.ConfirmLabel);
        Assert.Equal("Here's exactly what SavePoint will change before the game starts.", dialog.Subtitle);
    }

    [Fact]
    public async Task A_profile_that_is_already_in_place_says_so_instead_of_starting_a_job()
    {
        await _f.Flow.BeginSwitchAsync(FlowRig.Skyrim, LaunchRig.ModdedTip);

        var card = _f.Inline<ErrorCardViewModel>();
        Assert.Equal("Already in place", card.Title);
        Assert.Equal("Modded playthrough - 7-2-26 is already loaded in your game folders, so nothing changed. The game was not started.", card.Body);
        Assert.Equal(["Dismiss"], card.Actions.Select(action => action.Label));
        Assert.Empty(Switches());
        Assert.Empty(_f.Starts());
        Assert.False(_f.Flow.IsActive(FlowRig.Skyrim));
    }

    [Fact]
    public async Task A_failed_switch_shows_its_card_and_try_again_switches_again_instead_of_launching()
    {
        await SwitchWithProgressAsync();
        var progress = _f.Progress;
        var job = _f.Backend.Jobs.LastStarted(JobKind.Switch);

        _f.Backend.Jobs.Fail(job.JobId, ErrorInfo.From(new GitOperationException(_file, "in use")));

        Assert.True(progress.IsClosed);
        var card = _f.Inline<ErrorCardViewModel>();
        Assert.Equal(["Try again", "Dismiss"], card.Actions.Select(action => action.Label));
        Assert.False(_f.Flow.IsActive(FlowRig.Skyrim));
        Assert.Empty(_f.Rig.Toasts.Toasts);

        FlowRig.Press(card, "Try again");

        Assert.Equal(2, Switches().Count);
        Assert.Empty(_f.Starts());
    }

    [Fact]
    public async Task Stopping_a_switch_asks_the_service_to_cancel_the_job_and_a_cancelled_switch_offers_to_start_over_as_a_switch()
    {
        await SwitchWithProgressAsync();
        var job = _f.Backend.Jobs.LastStarted(JobKind.Switch);

        await _f.Progress.CancelCommand.ExecuteAsync(null);
        Assert.True(_f.Backend.Jobs.LastStarted(JobKind.Switch).CancelRequested);
        _f.Backend.Jobs.CompleteCancelled(job.JobId);

        var card = _f.Inline<ErrorCardViewModel>();
        Assert.Equal(["Try again", "Dismiss"], card.Actions.Select(action => action.Label));
        Assert.Empty(_f.Rig.Toasts.Toasts);
        var before = _f.Backend.Calls.To<ILaunchApi>(nameof(ILaunchApi.GetPreviewAsync)).Count;

        FlowRig.Press(card, "Try again");

        Assert.True(_f.Backend.Calls.To<ILaunchApi>(nameof(ILaunchApi.GetPreviewAsync)).Count > before);
        Assert.Empty(_f.Starts());
        Assert.Equal(2, Switches().Count);
    }
}
