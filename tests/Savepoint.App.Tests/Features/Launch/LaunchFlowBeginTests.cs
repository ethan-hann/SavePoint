using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Features.Launch;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.Launch;

public class LaunchFlowBeginTests : IDisposable
{
    private readonly FlowRig _f = new();

    public void Dispose() => _f.Dispose();

    private static async Task<FakeBackend> WithPreviewAsync(Func<LaunchPreview, LaunchPreview> change, LaunchTarget target)
    {
        var source = FakeAssert.Ok(await FakeBackend.Sample().Launch.GetPreviewAsync(SampleScenario.SkyrimId, target));
        return new FakeBackend(ScenarioBuilder.Sample().WithLaunchPreview(change(source)));
    }

    // Holds the scan until the test lets it go, the way a large modded install holds it for seconds.
    private TaskCompletionSource HoldPreview()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _f.Backend.Launch.PreviewGate = token => release.Task.WaitAsync(token);
        return release;
    }

    // The scan's continuation runs on another thread once it is released, so the dialog appears a moment later.
    private async Task DialogShownAsync()
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (!_f.Dialogs.Shown.OfType<LaunchDialogViewModel>().Any())
        {
            Assert.True(DateTime.UtcNow < until, "The launch dialog never appeared.");
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task Play_says_it_is_checking_at_once_however_long_the_scan_takes()
    {
        var release = HoldPreview();

        var begin = _f.Play(LaunchRig.SurvivalTip);

        Assert.False(begin.IsCompleted);
        var card = _f.Inline<LaunchCheckingViewModel>();
        Assert.Equal("Checking your files…", card.Title);
        Assert.Equal("This can take a moment on a large game.", card.Hint);
        Assert.True(_f.Flow.IsActive(FlowRig.Skyrim));

        release.SetResult();
        await DialogShownAsync();
        _f.LaunchDialog.CancelCommand.Execute(null);
        await begin;
    }

    [Fact]
    public async Task The_checking_card_gives_way_to_the_dialog_when_the_scan_is_done()
    {
        var release = HoldPreview();
        var begin = _f.Play(LaunchRig.SurvivalTip);
        Assert.IsType<LaunchCheckingViewModel>(_f.Flow.InlineContentFor(FlowRig.Skyrim));

        release.SetResult();
        await DialogShownAsync();

        Assert.Null(_f.Flow.InlineContentFor(FlowRig.Skyrim));
        _f.LaunchDialog.CancelCommand.Execute(null);
        await begin;
    }

    [Fact]
    public async Task The_checking_card_gives_way_to_the_banner_when_nothing_changes()
    {
        var release = HoldPreview();
        var begin = _f.Play(LaunchRig.ModdedTip);

        release.SetResult();
        await begin;

        _ = _f.Inline<LaunchBannerViewModel>();
    }

    [Fact]
    public async Task Cancel_on_the_checking_card_stops_the_scan_frees_the_game_and_shows_no_error()
    {
        _ = HoldPreview();
        var begin = _f.Play(LaunchRig.SurvivalTip);

        _f.Inline<LaunchCheckingViewModel>().CancelCommand.Execute(null);
        await begin;

        Assert.Null(_f.Flow.InlineContentFor(FlowRig.Skyrim));
        Assert.False(_f.Flow.IsActive(FlowRig.Skyrim));
        Assert.Empty(_f.Dialogs.Shown);
        Assert.Empty(_f.Rig.Toasts.Toasts);
        Assert.Empty(_f.Starts());
    }

    [Fact]
    public async Task A_scan_that_fails_replaces_the_checking_card_with_the_failure_card()
    {
        var release = HoldPreview();
        _f.Backend.FailNext<ILaunchApi>(nameof(ILaunchApi.GetPreviewAsync), SampleScenario.FileInUseError);
        var begin = _f.Play(LaunchRig.SurvivalTip);

        release.SetResult();
        await begin;

        _ = _f.Inline<ErrorCardViewModel>();
        Assert.False(_f.Flow.IsActive(FlowRig.Skyrim));
    }

    [Fact]
    public async Task When_nothing_changes_the_game_starts_at_once_and_the_banner_says_so()
    {
        await _f.PlayModdedAsync();

        var start = Assert.Single(_f.Starts());
        Assert.Equal([FlowRig.Skyrim, LaunchRig.ModdedTip], start.Args);
        Assert.Empty(_f.Dialogs.Shown);
        var banner = _f.Inline<LaunchBannerViewModel>();
        Assert.Equal("Everything was already in place, so Skyrim Special Edition is starting now", banner.Title);
        Assert.Equal("Modded playthrough - 7-2-26 · saves from \"Before the final boss\"", banner.Detail);
        Assert.True(_f.Flow.IsActive(FlowRig.Skyrim));
    }

    [Fact]
    public async Task The_banner_gives_way_to_the_running_card_once_the_game_runs()
    {
        await _f.PlayModdedAsync();
        var banner = _f.Inline<LaunchBannerViewModel>();

        _f.Backend.Launch.ReportRunning(FlowRig.Skyrim, SampleScenario.RunningSince);

        var card = _f.Inline<RunningCardViewModel>();
        Assert.NotSame(banner, card);
        Assert.Equal("Skyrim Special Edition is running", card.Title);
        Assert.Equal("Modded playthrough - 7-2-26 · started 41 minutes ago", card.Subtitle);
        Assert.True(_f.Flow.IsActive(FlowRig.Skyrim));
    }

    [Fact]
    public async Task Something_to_switch_opens_the_dialog_and_nothing_starts_until_it_is_confirmed()
    {
        var begin = _f.Play(LaunchRig.SurvivalTip);

        Assert.False(begin.IsCompleted);
        var dialog = _f.LaunchDialog;
        Assert.Equal("Switch to \"Survival test - 5-14-26\"?", dialog.Title);
        Assert.Equal("Skyrim Special Edition", dialog.GameName);
        Assert.Empty(_f.Starts());
        Assert.True(_f.Flow.IsActive(FlowRig.Skyrim));
        Assert.Null(_f.Flow.InlineContentFor(FlowRig.Skyrim));

        dialog.CancelCommand.Execute(null);
        await begin;
    }

    [Fact]
    public async Task Cancelling_the_dialog_starts_nothing_and_frees_the_game()
    {
        var begin = _f.Play(LaunchRig.SurvivalTip);

        _f.LaunchDialog.CancelCommand.Execute(null);
        await begin;

        Assert.Empty(_f.Starts());
        Assert.False(_f.Flow.IsActive(FlowRig.Skyrim));
        Assert.Null(_f.Flow.InlineContentFor(FlowRig.Skyrim));
    }

    [Fact]
    public async Task Escape_on_the_dialog_cancels_it_too()
    {
        var begin = _f.Play(LaunchRig.SurvivalTip);

        _f.LaunchDialog.RequestDismiss();
        await begin;

        Assert.Empty(_f.Starts());
        Assert.False(_f.Flow.IsActive(FlowRig.Skyrim));
    }

    [Fact]
    public async Task Confirming_the_dialog_starts_the_chosen_target_and_opens_the_progress_dialog()
    {
        var begin = _f.Play(LaunchRig.SurvivalTip);
        var dialog = _f.LaunchDialog;
        _f.Dialogs.AnswerConfirm(true);
        await dialog.UnsavedBlocks[0].DiscardCommand.ExecuteAsync(null);
        dialog.SelectedSave = dialog.SaveOptions[1];

        dialog.SwitchCommand.Execute(null);
        await begin;

        var start = Assert.Single(_f.Starts());
        Assert.Equal([FlowRig.Skyrim, new LaunchTarget.Save(SampleScenario.SurvivalId, SampleScenario.FirstNight)], start.Args);
        var progress = _f.Progress;
        Assert.Equal("Getting Skyrim Special Edition ready…", progress.Title);
        Assert.Equal("Putting version 1.6.640 and its 189 mod files back in place.", progress.Explanation);
        Assert.True(_f.Flow.IsActive(FlowRig.Skyrim));
    }

    [Fact]
    public async Task When_only_the_save_differs_and_the_preference_is_on_the_game_starts_without_the_dialog()
    {
        await _f.PlayWithProgressAsync();

        Assert.Empty(_f.Dialogs.Shown.OfType<LaunchDialogViewModel>());
        var start = Assert.Single(_f.Starts());
        Assert.Equal([FlowRig.Skyrim, LaunchRig.ClearedBlackreachPoint], start.Args);
        Assert.Single(_f.Dialogs.Shown.OfType<SwitchProgressDialogViewModel>());
    }

    [Fact]
    public async Task When_only_the_save_differs_but_the_preference_is_off_the_dialog_still_asks()
    {
        var begin = _f.Play(LaunchRig.ClearedBlackreachPoint);

        Assert.Single(_f.Dialogs.Shown.OfType<LaunchDialogViewModel>());
        Assert.Empty(_f.Starts());
        _f.LaunchDialog.CancelCommand.Execute(null);
        await begin;
    }

    [Fact]
    public async Task Unsaved_changes_always_bring_the_dialog_back_even_when_the_preference_is_on()
    {
        using var rig = new FlowRig(await WithPreviewAsync(
            preview => preview with { External = preview.External! with { Dirty = true, DirtyFileCount = 2, DirtyOwnerProfileId = SampleScenario.ModdedId } },
            LaunchRig.ClearedBlackreachPoint));
        FakeAssert.Ok(await rig.Backend.Settings.SaveAsync(SampleScenario.Settings with { SkipConfirmWhenOnlySaveChanges = true }));

        var begin = rig.Play(LaunchRig.ClearedBlackreachPoint);

        Assert.Single(rig.Dialogs.Shown.OfType<LaunchDialogViewModel>());
        Assert.Empty(rig.Starts());
        rig.LaunchDialog.CancelCommand.Execute(null);
        await begin;
    }

    [Fact]
    public async Task The_dialog_starts_from_the_saved_preference_and_reports_a_change()
    {
        FakeAssert.Ok(await _f.Backend.Settings.SaveAsync(SampleScenario.Settings with { SkipConfirmWhenOnlySaveChanges = false }));
        var begin = _f.Play(LaunchRig.SurvivalTip);
        var dialog = _f.LaunchDialog;
        Assert.False(dialog.DontAskAgain);

        dialog.DontAskAgain = true;

        Assert.True(Assert.Single(_f.SavedSettings).SkipConfirmWhenOnlySaveChanges);
        dialog.CancelCommand.Execute(null);
        await begin;
    }

    [Fact]
    public async Task A_second_play_while_the_first_is_underway_is_ignored()
    {
        await _f.PlayModdedAsync();

        await _f.Play(LaunchRig.ModdedTip);

        Assert.Single(_f.Starts());
    }

    [Fact]
    public async Task A_second_play_while_the_dialog_is_open_is_ignored_too()
    {
        var begin = _f.Play(LaunchRig.SurvivalTip);

        var second = _f.Play(LaunchRig.VanillaTip);

        Assert.True(second.IsCompleted);
        Assert.Single(_f.Dialogs.Shown.OfType<LaunchDialogViewModel>());
        _f.LaunchDialog.CancelCommand.Execute(null);
        await begin;
    }

    [Fact]
    public async Task A_preview_that_fails_shows_a_card_the_user_can_dismiss()
    {
        _f.Backend.FailNext<ILaunchApi>(nameof(ILaunchApi.GetPreviewAsync), SampleScenario.FolderGoneError);

        await _f.Play(LaunchRig.ModdedTip);

        var card = _f.Inline<ErrorCardViewModel>();
        Assert.Equal("This game's folder has moved or is gone", card.Title);
        Assert.False(_f.Flow.IsActive(FlowRig.Skyrim));
        Assert.Empty(_f.Starts());

        FlowRig.Press(card, "Dismiss");
        Assert.Null(_f.Flow.InlineContentFor(FlowRig.Skyrim));
    }

    [Fact]
    public async Task A_failed_preview_can_be_tried_again()
    {
        _f.Backend.FailNext<ILaunchApi>(nameof(ILaunchApi.GetPreviewAsync), SampleScenario.SavesOutOfRoomError);
        await _f.Play(LaunchRig.ModdedTip);
        var card = _f.Inline<ErrorCardViewModel>();

        FlowRig.Press(card, "Try again");

        Assert.Single(_f.Starts());
        Assert.IsType<LaunchBannerViewModel>(_f.Flow.InlineContentFor(FlowRig.Skyrim));
    }

    [Fact]
    public async Task A_start_the_service_refuses_shows_a_card_and_the_game_is_free_again()
    {
        _f.Backend.FailNext<ILaunchApi>(nameof(ILaunchApi.StartAsync), ErrorInfo.From(new LaunchAlreadyRunningException(FlowRig.Skyrim)));

        await _f.PlayModdedAsync();

        var card = _f.Inline<ErrorCardViewModel>();
        Assert.Equal("The game is running", card.Title);
        Assert.Equal("This game is already running.", card.Body);
        Assert.False(_f.Flow.IsActive(FlowRig.Skyrim));
        Assert.Equal(["Dismiss"], card.Actions.Select(a => a.Label));
    }

    [Fact]
    public async Task A_preview_the_service_does_not_have_reads_as_a_general_failure()
    {
        await _f.Play(new LaunchTarget.ProfileTip("no-such-profile"));

        var card = _f.Inline<ErrorCardViewModel>();
        Assert.Equal("Something went wrong", card.Title);
        Assert.False(_f.Flow.IsActive(FlowRig.Skyrim));
    }

    [Fact]
    public async Task Two_games_run_independently()
    {
        var satisfactory = new LaunchTarget.ProfileTip(SampleScenario.MainFactoryId);

        await _f.PlayModdedAsync();
        await _f.Play(satisfactory, SampleScenario.SatisfactoryId);

        Assert.True(_f.Flow.IsActive(SampleScenario.SkyrimId));
        Assert.True(_f.Flow.IsActive(SampleScenario.SatisfactoryId));
        Assert.False(_f.Flow.IsActive(SampleScenario.CaretakerId));
        Assert.Equal([SampleScenario.SatisfactoryId, SampleScenario.SkyrimId], _f.Flow.ActiveGameIds);
        Assert.NotSame(_f.Flow.InlineContentFor(SampleScenario.SkyrimId), _f.Flow.InlineContentFor(SampleScenario.SatisfactoryId));
        Assert.Equal(2, _f.Starts().Count);
    }

    [Fact]
    public async Task Changes_to_the_inline_content_are_reported_for_the_game_they_belong_to()
    {
        await _f.PlayModdedAsync();
        _f.Backend.Launch.ReportRunning(FlowRig.Skyrim, SampleScenario.RunningSince);

        Assert.NotEmpty(_f.InlineChanges);
        Assert.All(_f.InlineChanges, gameId => Assert.Equal(FlowRig.Skyrim, gameId));
    }

    [Fact]
    public async Task The_active_games_property_announces_when_the_set_changes()
    {
        var changed = new List<string?>();
        _f.Flow.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await _f.PlayModdedAsync();

        Assert.Contains(nameof(LaunchFlowViewModel.ActiveGameIds), changed);
    }
}
