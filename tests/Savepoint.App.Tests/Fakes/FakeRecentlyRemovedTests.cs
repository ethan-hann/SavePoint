using Savepoint.Core.Api;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Fakes;

// The fake lists and brings back recently removed snapshots as the real backend does, so a screen built against it behaves the same.
public class FakeRecentlyRemovedTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static FakeBackend Seeded() => new(RemovedSnapshotSamples.Scenario());

    [Fact]
    public async Task A_game_with_none_removed_lists_none()
    {
        var backend = FakeBackend.Mockup();

        var removed = FakeAssert.Ok(await backend.History.GetRecentlyRemovedAsync(MockupScenario.SkyrimId, Ct));

        Assert.Equal(MockupScenario.SkyrimId, removed.GameId);
        Assert.Empty(removed.Snapshots);
    }

    [Fact]
    public async Task A_game_lists_its_removed_snapshots_most_recently_removed_first_and_leaves_other_games_out()
    {
        var backend = Seeded();

        var skyrim = FakeAssert.Ok(await backend.History.GetRecentlyRemovedAsync(MockupScenario.SkyrimId, Ct)).Snapshots;
        var satisfactory = FakeAssert.Ok(await backend.History.GetRecentlyRemovedAsync(MockupScenario.SatisfactoryId, Ct)).Snapshots;

        Assert.Equal([RemovedSnapshotSamples.Continued, RemovedSnapshotSamples.Replaced], skyrim);
        Assert.Equal([RemovedSnapshotSamples.FromDeletedProfile], satisfactory);
    }

    [Fact]
    public async Task An_unknown_game_fails_with_the_game_not_found_error()
    {
        var backend = Seeded();

        var result = await backend.History.GetRecentlyRemovedAsync("no-such-game", Ct);

        Assert.False(result.Ok);
        Assert.Equal(new GameNotFoundException("no-such-game").MessageId, result.Error?.MessageId);
    }

    [Fact]
    public async Task Bringing_a_snapshot_back_makes_a_profile_that_owns_it_and_takes_it_off_the_list()
    {
        var backend = Seeded();

        var brought = FakeAssert.Ok(await backend.Profiles.BringBackAsync(MockupScenario.SkyrimId, RemovedSnapshotSamples.Continued.Id, "Boss fight again"));

        Assert.Equal("Boss fight again", brought.Name);
        Assert.Equal(MockupScenario.SkyrimId, brought.GameId);
        var history = FakeAssert.Ok(await backend.History.GetAsync(MockupScenario.SkyrimId, brought.Id, Ct));
        var point = Assert.Single(history.Points);
        Assert.Equal(RemovedSnapshotSamples.Continued.Id, point.Id);
        Assert.Equal("Before the final boss", point.Name);
        Assert.Equal(RemovedSnapshotSamples.Continued.Install, point.Install);
        Assert.Equal(brought.Name, point.ProfileName);
        var stats = FakeAssert.Ok(await backend.Profiles.GetRowStatsAsync(MockupScenario.SkyrimId, Ct)).Single(row => row.ProfileId == brought.Id);
        Assert.Equal(1, stats.SnapshotCount);
        var listed = FakeAssert.Ok(await backend.History.GetRecentlyRemovedAsync(MockupScenario.SkyrimId, Ct)).Snapshots;
        Assert.Equal([RemovedSnapshotSamples.Replaced], listed);
    }

    [Fact]
    public async Task A_snapshot_that_is_not_listed_or_a_blank_name_fails_with_the_errors_the_real_backend_gives()
    {
        var backend = Seeded();

        var missing = await backend.Profiles.BringBackAsync(MockupScenario.SkyrimId, new HistoryPointId("no-such-snapshot"), "Name");
        var blank = await backend.Profiles.BringBackAsync(MockupScenario.SkyrimId, RemovedSnapshotSamples.Continued.Id, "  ");
        var otherGame = await backend.Profiles.BringBackAsync(MockupScenario.SatisfactoryId, RemovedSnapshotSamples.Continued.Id, "Name");

        Assert.Equal("error.removed.notFound", missing.Error?.MessageId);
        Assert.Equal("error.validation.invalidPayload", blank.Error?.MessageId);
        Assert.Equal("error.removed.notFound", otherGame.Error?.MessageId);
        Assert.Equal(2, FakeAssert.Ok(await backend.History.GetRecentlyRemovedAsync(MockupScenario.SkyrimId, Ct)).Snapshots.Count);
    }

    [Fact]
    public async Task The_free_up_space_preview_counts_the_removed_snapshots_of_a_game_and_of_every_game()
    {
        var backend = Seeded();

        var skyrim = FakeAssert.Ok(await backend.Games.GetFreeUpSpacePreviewAsync(MockupScenario.SkyrimId, Ct));
        var caretaker = FakeAssert.Ok(await backend.Games.GetFreeUpSpacePreviewAsync(MockupScenario.CaretakerId, Ct));
        var all = FakeAssert.Ok(await backend.Settings.GetFreeUpSpacePreviewAsync(Ct));
        var unknown = await backend.Games.GetFreeUpSpacePreviewAsync("no-such-game", Ct);

        Assert.Equal(2, skyrim.RecentlyRemovedSnapshots);
        Assert.Equal(0, caretaker.RecentlyRemovedSnapshots);
        Assert.Equal(3, all.RecentlyRemovedSnapshots);
        Assert.Equal(new GameNotFoundException("no-such-game").MessageId, unknown.Error?.MessageId);
    }

    [Fact]
    public async Task Freeing_up_space_for_a_game_deletes_its_removed_snapshots_when_the_job_has_run_and_only_that_games()
    {
        var backend = Seeded();
        var preview = FakeAssert.Ok(await backend.Games.GetFreeUpSpacePreviewAsync(MockupScenario.SkyrimId, Ct));

        var started = FakeAssert.Ok(await backend.Games.OptimizeStorageAsync(MockupScenario.SkyrimId, preview.Token));
        Assert.Equal(2, FakeAssert.Ok(await backend.History.GetRecentlyRemovedAsync(MockupScenario.SkyrimId, Ct)).Snapshots.Count);
        backend.Jobs.Complete(started.JobId);

        Assert.Empty(FakeAssert.Ok(await backend.History.GetRecentlyRemovedAsync(MockupScenario.SkyrimId, Ct)).Snapshots);
        Assert.Single(FakeAssert.Ok(await backend.History.GetRecentlyRemovedAsync(MockupScenario.SatisfactoryId, Ct)).Snapshots);
    }

    [Fact]
    public async Task Freeing_up_space_for_every_game_deletes_every_games_removed_snapshots_when_the_job_has_run()
    {
        var backend = Seeded();
        var preview = FakeAssert.Ok(await backend.Settings.GetFreeUpSpacePreviewAsync(Ct));

        var started = FakeAssert.Ok(await backend.Settings.OptimizeAllStorageAsync(preview.Token));
        backend.Jobs.Complete(started.JobId);

        Assert.Equal(0, FakeAssert.Ok(await backend.Settings.GetFreeUpSpacePreviewAsync(Ct)).RecentlyRemovedSnapshots);
    }

    [Fact]
    public async Task Freeing_up_space_deletes_only_the_snapshots_its_preview_counted_and_a_run_without_one_deletes_none()
    {
        var backend = Seeded();
        var preview = FakeAssert.Ok(await backend.Games.GetFreeUpSpacePreviewAsync(MockupScenario.SkyrimId, Ct));
        var later = RemovedSnapshotSamples.Continued with { Id = new HistoryPointId("pt-removed-after-the-preview") };
        backend.Script(() => backend.State.Removed.Add((MockupScenario.SkyrimId, later)));

        var blind = FakeAssert.Ok(await backend.Games.OptimizeStorageAsync(MockupScenario.SkyrimId));
        backend.Jobs.Complete(blind.JobId);
        Assert.Equal(3, FakeAssert.Ok(await backend.History.GetRecentlyRemovedAsync(MockupScenario.SkyrimId, Ct)).Snapshots.Count);

        var counted = FakeAssert.Ok(await backend.Games.OptimizeStorageAsync(MockupScenario.SkyrimId, preview.Token));
        backend.Jobs.Complete(counted.JobId);

        var left = Assert.Single(FakeAssert.Ok(await backend.History.GetRecentlyRemovedAsync(MockupScenario.SkyrimId, Ct)).Snapshots);
        Assert.Equal(later.Id, left.Id);
    }

    [Fact]
    public async Task Deleting_the_profile_a_snapshot_left_keeps_the_snapshot_listed_without_naming_a_profile()
    {
        var backend = Seeded();

        FakeAssert.Ok(await backend.Profiles.DeleteAsync(MockupScenario.SkyrimId, MockupScenario.VanillaId));
        var listed = FakeAssert.Ok(await backend.History.GetRecentlyRemovedAsync(MockupScenario.SkyrimId, Ct)).Snapshots;

        Assert.Equal(2, listed.Count);
        var replaced = listed.Single(snapshot => snapshot.Id == RemovedSnapshotSamples.Replaced.Id);
        Assert.Null(replaced.FromProfileId);
        Assert.Null(replaced.FromProfileName);
        Assert.Equal(MockupScenario.ModdedId, listed.Single(snapshot => snapshot.Id == RemovedSnapshotSamples.Continued.Id).FromProfileId);
    }
}
