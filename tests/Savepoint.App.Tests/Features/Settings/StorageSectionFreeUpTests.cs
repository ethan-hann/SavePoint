using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.Common;
using Savepoint.Core.Api;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.Settings;

// Free up space deletes recently removed snapshots for good, so when there are some the row says how many and asks first; when there are none it
// runs at once, as it always did.
public class StorageSectionFreeUpTests
{
    private static async Task<SettingsRig> SeededAsync() => await SettingsRig.ShownAsync(new FakeBackend(RemovedSnapshotSamples.Scenario()));

    [Fact]
    public async Task The_row_says_before_anything_runs_how_many_recently_removed_snapshots_free_up_space_would_delete_for_good()
    {
        var rig = await SeededAsync();

        Assert.Equal("Free up space would delete 3 recently removed snapshots for good. You can't bring them back.", rig.Screen.Storage.OptimizePreview);
        Assert.Equal(0, rig.Calls<ISettingsApi>(nameof(ISettingsApi.OptimizeAllStorageAsync)));
    }

    // ---- Saying how much space comes back

    [Fact]
    public async Task The_row_adds_about_how_much_space_comes_back_once_the_backend_has_worked_it_out()
    {
        var backend = new FakeBackend(RemovedSnapshotSamples.Scenario());
        backend.State.FreeUpEstimate = 1_200_000_000;

        var rig = await SettingsRig.ShownAsync(backend);

        Assert.Equal("Free up space would delete 3 recently removed snapshots for good. Deleting them frees up about 1.2 GB. You can't bring them back.", rig.Screen.Storage.OptimizePreview);
    }

    [Fact]
    public async Task While_the_size_is_being_worked_out_the_row_has_the_count_alone_and_the_screen_is_ready()
    {
        var backend = new FakeBackend(RemovedSnapshotSamples.Scenario());
        backend.State.FreeUpEstimate = 1_200_000_000;
        var hold = backend.Settings.EstimateHold = new TaskCompletionSource();

        var rig = await SettingsRig.ShownAsync(backend);
        var whileWaiting = rig.Screen.Storage.OptimizePreview;
        hold.SetResult();

        Assert.Equal("Free up space would delete 3 recently removed snapshots for good. You can't bring them back.", whileWaiting);
        Assert.True(rig.Screen.IsLoaded);
        Assert.True(await UiWait.UntilAsync(() => rig.Screen.Storage.OptimizePreview.Contains("about 1.2 GB", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Leaving_the_screen_stops_a_size_that_is_still_being_worked_out()
    {
        var backend = new FakeBackend(RemovedSnapshotSamples.Scenario());
        backend.State.FreeUpEstimate = 1_200_000_000;
        var hold = backend.Settings.EstimateHold = new TaskCompletionSource();
        var rig = await SettingsRig.ShownAsync(backend);
        var asked = backend.Settings.EstimateTokens.ToList();

        rig.Screen.OnHidden();
        hold.SetResult();

        Assert.NotEmpty(asked);
        Assert.All(asked, token => Assert.True(token.IsCancellationRequested));
        await Task.Yield();
        Assert.DoesNotContain("about", rig.Screen.Storage.OptimizePreview, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Closing_the_screen_stops_a_size_that_is_still_being_worked_out()
    {
        var backend = new FakeBackend(RemovedSnapshotSamples.Scenario());
        backend.Settings.EstimateHold = new TaskCompletionSource();
        var rig = await SettingsRig.ShownAsync(backend);
        var asked = backend.Settings.EstimateTokens.ToList();

        rig.Screen.Dispose();

        Assert.NotEmpty(asked);
        Assert.All(asked, token => Assert.True(token.IsCancellationRequested));
    }

    [Fact]
    public async Task A_size_that_cannot_be_worked_out_leaves_the_row_with_the_count_alone_and_shows_no_error()
    {
        var backend = new FakeBackend(RemovedSnapshotSamples.Scenario());
        backend.FailNext<ISettingsApi>(nameof(ISettingsApi.EstimateFreeUpSpaceAsync), ErrorInfo.From(new InvalidOperationException("Busy.")));

        var rig = await SettingsRig.ShownAsync(backend);

        Assert.Equal("Free up space would delete 3 recently removed snapshots for good. You can't bring them back.", rig.Screen.Storage.OptimizePreview);
        Assert.Empty(rig.Toasts.Toasts);
        Assert.Null(rig.Screen.Storage.Problem);
    }

    [Fact]
    public async Task No_size_is_asked_for_when_nothing_is_removed()
    {
        var backend = FakeBackend.Sample();
        backend.State.FreeUpEstimate = 1_200_000_000;

        var rig = await SettingsRig.ShownAsync(backend);

        Assert.Equal(0, rig.Calls<ISettingsApi>(nameof(ISettingsApi.EstimateFreeUpSpaceAsync)));
        Assert.DoesNotContain("about", rig.Screen.Storage.OptimizePreview, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_now_names_the_size_in_its_question_when_it_is_known()
    {
        var backend = new FakeBackend(RemovedSnapshotSamples.Scenario());
        backend.State.FreeUpEstimate = 1_200_000_000;
        var rig = await SettingsRig.ShownAsync(backend);
        rig.Dialogs.AnswerConfirm(true);

        await rig.Screen.Storage.RunNowCommand.ExecuteAsync(null);

        Assert.Equal("This also deletes 3 recently removed snapshots for good. Deleting them frees up about 1.2 GB. You can't bring them back.", Assert.Single(rig.Dialogs.Confirmations).Message);
    }

    [Fact]
    public async Task Run_now_does_not_wait_for_a_size_that_is_slow()
    {
        var backend = new FakeBackend(RemovedSnapshotSamples.Scenario());
        backend.State.FreeUpEstimate = 1_200_000_000;
        backend.Settings.EstimateHold = new TaskCompletionSource();
        var rig = await SettingsRig.ShownAsync(backend);
        rig.Dialogs.AnswerConfirm(true);

        await rig.Screen.Storage.RunNowCommand.ExecuteAsync(null);

        Assert.Equal("This also deletes 3 recently removed snapshots for good. You can't bring them back.", Assert.Single(rig.Dialogs.Confirmations).Message);
        Assert.True(rig.Screen.Storage.IsOptimizing);
    }

    // The estimate reads the store the run is about to repack and prune, so an answer that arrives after the run starts says nothing.
    [Fact]
    public async Task A_yes_drops_the_size_still_being_worked_out_so_it_never_shows_in_the_row()
    {
        var backend = new FakeBackend(RemovedSnapshotSamples.Scenario());
        backend.State.FreeUpEstimate = 1_200_000_000;
        var hold = backend.Settings.EstimateHold = new TaskCompletionSource();
        var rig = await SettingsRig.ShownAsync(backend);
        rig.Dialogs.AnswerConfirm(true);
        await rig.Screen.Storage.RunNowCommand.ExecuteAsync(null);
        Assert.True(rig.Screen.Storage.IsOptimizing);

        hold.SetResult();

        Assert.Equal("Free up space would delete 3 recently removed snapshots for good. You can't bring them back.", rig.Screen.Storage.OptimizePreview);
    }

    [Fact]
    public async Task The_row_reads_in_the_singular_for_one_recently_removed_snapshot()
    {
        var scenario = ScenarioBuilder.Sample().WithRemovedSnapshot(SampleScenario.SkyrimId, RemovedSnapshotSamples.Continued);
        var rig = await SettingsRig.ShownAsync(new FakeBackend(scenario));

        Assert.Equal("Free up space would delete 1 recently removed snapshot for good. You can't bring it back.", rig.Screen.Storage.OptimizePreview);
    }

    [Fact]
    public async Task The_row_says_when_nothing_would_be_deleted_for_good()
    {
        var rig = await SettingsRig.ShownAsync();

        Assert.Equal("Nothing is in Recently removed, so Free up space would not delete a snapshot for good.", rig.Screen.Storage.OptimizePreview);
    }

    [Fact]
    public async Task The_row_is_read_again_when_a_run_ends()
    {
        var rig = await SeededAsync();
        rig.Dialogs.AnswerConfirm(true);
        await rig.Screen.Storage.RunNowCommand.ExecuteAsync(null);

        rig.Backend.Jobs.Complete(rig.Backend.Jobs.LastStarted(JobKind.Compact).JobId);

        Assert.Equal("Nothing is in Recently removed, so Free up space would not delete a snapshot for good.", rig.Screen.Storage.OptimizePreview);
    }

    [Fact]
    public async Task A_count_that_cannot_be_read_leaves_the_row_without_a_line_and_the_button_working()
    {
        var backend = new FakeBackend(RemovedSnapshotSamples.Scenario());
        backend.FailNext<ISettingsApi>(nameof(ISettingsApi.GetFreeUpSpacePreviewAsync), ErrorInfo.From(new InvalidOperationException("Busy.")));

        var rig = await SettingsRig.ShownAsync(backend);

        Assert.Equal(string.Empty, rig.Screen.Storage.OptimizePreview);
        Assert.True(rig.Screen.Storage.RunNowCommand.CanExecute(null));
    }

    [Fact]
    public async Task With_removed_snapshots_run_now_asks_first_naming_how_many_it_deletes_and_runs_after_a_yes()
    {
        var rig = await SeededAsync();
        rig.Dialogs.AnswerConfirm(true);

        await rig.Screen.Storage.RunNowCommand.ExecuteAsync(null);

        var question = Assert.Single(rig.Dialogs.Confirmations);
        Assert.Equal("Free up space?", question.Title);
        Assert.Equal("This also deletes 3 recently removed snapshots for good. You can't bring them back.", question.Message);
        Assert.Equal("Delete 3 snapshots and free up space", question.ConfirmLabel);
        Assert.True(question.Destructive);
        Assert.Equal(1, rig.Calls<ISettingsApi>(nameof(ISettingsApi.OptimizeAllStorageAsync)));
        Assert.True(rig.Screen.Storage.IsOptimizing);
    }

    [Fact]
    public async Task The_run_it_starts_carries_the_records_the_player_was_asked_about()
    {
        var rig = await SeededAsync();
        rig.Dialogs.AnswerConfirm(true);
        var asked = FakeAssert.Ok(await rig.Backend.Settings.GetFreeUpSpacePreviewAsync(TestContext.Current.CancellationToken)).Token;

        await rig.Screen.Storage.RunNowCommand.ExecuteAsync(null);

        var run = Assert.Single(rig.Backend.Calls.To<ISettingsApi>(nameof(ISettingsApi.OptimizeAllStorageAsync)));
        Assert.Equal(asked.Records, Assert.IsType<FreeUpSpaceToken>(run.Args[0]).Records);
        Assert.Equal(3, asked.Records.Count);
    }

    [Fact]
    public async Task One_removed_snapshot_reads_in_the_singular()
    {
        var scenario = ScenarioBuilder.Sample().WithRemovedSnapshot(SampleScenario.SkyrimId, RemovedSnapshotSamples.Continued);
        var rig = await SettingsRig.ShownAsync(new FakeBackend(scenario));
        rig.Dialogs.AnswerConfirm(false);

        await rig.Screen.Storage.RunNowCommand.ExecuteAsync(null);

        var question = Assert.Single(rig.Dialogs.Confirmations);
        Assert.Equal("This also deletes 1 recently removed snapshot for good. You can't bring it back.", question.Message);
        Assert.Equal("Delete 1 snapshot and free up space", question.ConfirmLabel);
    }

    [Fact]
    public async Task Declining_starts_nothing_and_keeps_the_snapshots()
    {
        var rig = await SeededAsync();
        rig.Dialogs.AnswerConfirm(false);

        await rig.Screen.Storage.RunNowCommand.ExecuteAsync(null);

        Assert.Equal(0, rig.Calls<ISettingsApi>(nameof(ISettingsApi.OptimizeAllStorageAsync)));
        Assert.False(rig.Screen.Storage.IsOptimizing);
        Assert.Equal(3, FakeAssert.Ok(await rig.Backend.Settings.GetFreeUpSpacePreviewAsync(TestContext.Current.CancellationToken)).RecentlyRemovedSnapshots);
    }

    [Fact]
    public async Task With_nothing_removed_run_now_asks_nothing_and_runs_at_once()
    {
        var rig = await SettingsRig.ShownAsync();

        await rig.Screen.Storage.RunNowCommand.ExecuteAsync(null);

        Assert.Empty(rig.Dialogs.Confirmations);
        Assert.Equal(1, rig.Calls<ISettingsApi>(nameof(ISettingsApi.OptimizeAllStorageAsync)));
    }

    [Fact]
    public async Task A_count_that_cannot_be_read_says_so_and_starts_nothing()
    {
        var rig = await SeededAsync();
        rig.Backend.FailNext<ISettingsApi>(nameof(ISettingsApi.GetFreeUpSpacePreviewAsync), ErrorInfo.From(new InvalidOperationException("Busy.")));

        await rig.Screen.Storage.RunNowCommand.ExecuteAsync(null);

        Assert.Equal(ToastKind.Error, Assert.Single(rig.Toasts.Toasts).Message.Kind);
        Assert.Equal(0, rig.Calls<ISettingsApi>(nameof(ISettingsApi.OptimizeAllStorageAsync)));
        Assert.Empty(rig.Dialogs.Confirmations);
        Assert.False(rig.Screen.Storage.IsOptimizing);
    }
}
