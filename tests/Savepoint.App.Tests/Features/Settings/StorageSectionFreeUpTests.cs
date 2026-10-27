using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Tests.Fakes;
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
