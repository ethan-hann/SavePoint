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
    public async Task With_removed_snapshots_run_now_asks_first_naming_how_many_it_deletes_and_runs_after_a_yes()
    {
        var rig = await SeededAsync();
        rig.Dialogs.AnswerConfirm(true);

        await rig.Screen.Storage.RunNowCommand.ExecuteAsync(null);

        var question = Assert.Single(rig.Dialogs.Confirmations);
        Assert.Equal("Free up space?", question.Title);
        Assert.Equal("Freeing up space also deletes 3 recently removed snapshots for good. They are no longer part of any profile, and you can't bring them back afterward.", question.Message);
        Assert.Equal("Delete 3 snapshots and free up space", question.ConfirmLabel);
        Assert.True(question.Destructive);
        Assert.Equal(1, rig.Calls<ISettingsApi>(nameof(ISettingsApi.OptimizeAllStorageAsync)));
        Assert.True(rig.Screen.Storage.IsOptimizing);
    }

    [Fact]
    public async Task One_removed_snapshot_reads_in_the_singular()
    {
        var scenario = ScenarioBuilder.Mockup().WithRemovedSnapshot(MockupScenario.SkyrimId, RemovedSnapshotSamples.Continued);
        var rig = await SettingsRig.ShownAsync(new FakeBackend(scenario));
        rig.Dialogs.AnswerConfirm(false);

        await rig.Screen.Storage.RunNowCommand.ExecuteAsync(null);

        var question = Assert.Single(rig.Dialogs.Confirmations);
        Assert.Equal("Freeing up space also deletes 1 recently removed snapshot for good. It is no longer part of any profile, and you can't bring it back afterward.", question.Message);
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
