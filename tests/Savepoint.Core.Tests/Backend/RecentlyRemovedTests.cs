using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Backend;

// The snapshots "Continue from this session" and "Replace with this session" take off a profile are listed for the game until the player
// frees up space, with what History shows for a snapshot and the profile they came from, and they belong to no profile.
public class RecentlyRemovedTests
{
    [Fact(Timeout = 120_000)]
    public async Task A_game_that_removed_nothing_lists_nothing()
    {
        using var world = await RemovedWorld.CreateAsync();

        var removed = await world.ListAsync();

        Assert.Empty(removed);
    }

    [Fact(Timeout = 120_000)]
    public async Task A_stop_before_the_profile_moved_back_leaves_every_snapshot_on_it_and_none_in_the_list_and_a_repeat_lists_them()
    {
        var stopBeforeTheBranchMoves = 0;
        using var world = await RemovedWorld.CreateAsync(options => options with
        {
            Engine = new GitEngineOptions
            {
                InvocationObserver = invocation =>
                {
                    if (Volatile.Read(ref stopBeforeTheBranchMoves) == 1 && invocation.Arguments[0] == "update-ref" && invocation.Arguments.Any(argument => argument.StartsWith("refs/heads/profile/", StringComparison.Ordinal)))
                    {
                        throw new IOException("The disk reported an error.");
                    }
                },
            },
        });

        Volatile.Write(ref stopBeforeTheBranchMoves, 1);
        await Assert.ThrowsAsync<IOException>(world.ContinueFromFirstAsync);
        Volatile.Write(ref stopBeforeTheBranchMoves, 0);

        // The records were written first, so a later cleanup cannot lose a snapshot; the snapshots are still the profile's, so none is listed.
        Assert.Empty(await world.ListAsync());
        Assert.Equal(2, (await world.CommitsAsync(world.Profile.BranchName, RepoRole.Install)).Count);
        Assert.Equal(2, (await world.CommitsAsync(world.Profile.BranchName, RepoRole.External)).Count);
        await world.ContinueFromFirstAsync();
        Assert.Equal([world.SavesOnly, world.InstallOnly], (await world.ListAsync()).Select(snapshot => snapshot.Id));
    }

    [Fact(Timeout = 120_000)]
    public async Task Continuing_a_profile_from_an_older_snapshot_lists_each_snapshot_that_left_it_as_History_describes_one()
    {
        using var world = await RemovedWorld.CreateAsync();

        await world.ContinueFromFirstAsync();
        var removed = await world.ListAsync();

        Assert.Equal([world.SavesOnly, world.InstallOnly], removed.Select(snapshot => snapshot.Id));
        var saves = removed[0];
        Assert.Equal(HistoryPointKind.SaveOnly, saves.Kind);
        Assert.Equal("Second save", saves.Name);
        var install = removed[1];
        Assert.Equal(HistoryPointKind.VersionAndMods, install.Kind);
        Assert.Equal("Added a mod", install.Name);
        Assert.All(removed, snapshot =>
        {
            Assert.Equal(world.Profile.Id, snapshot.FromProfileId);
            Assert.Equal("Vanilla", snapshot.FromProfileName);
            Assert.Equal(RemovedSnapshotReason.ContinuedFromSession, snapshot.Reason);
            Assert.True(Math.Abs((DateTimeOffset.UtcNow - snapshot.RemovedAt).TotalMinutes) < 5);
            Assert.True(snapshot.CapturedAt < snapshot.RemovedAt);

            // A snapshot that recorded only saves still reads the install current when it was taken, which is the one a mod was added to.
            Assert.Equal(new InstallState(null, 1), snapshot.Install);
        });
    }

    [Fact(Timeout = 120_000)]
    public async Task Replacing_the_latest_snapshot_lists_what_each_folder_held_before()
    {
        using var world = await RemovedWorld.CreateAsync();

        await world.ReplaceLatestAsync("Replaced");
        var removed = await world.ListAsync();

        Assert.Equal([world.SavesOnly, world.InstallOnly], removed.Select(snapshot => snapshot.Id));
        Assert.All(removed, snapshot =>
        {
            Assert.Equal(RemovedSnapshotReason.ReplacedWithSession, snapshot.Reason);
            Assert.Equal("Vanilla", snapshot.FromProfileName);
        });
        var history = (await world.Workspace.History.GetAsync(world.Game.Id, null, world.Workspace.Token)).Value!;
        Assert.Equal(["Replaced", "Vanilla"], history.Points.Select(point => point.Name));
    }

    [Fact(Timeout = 120_000)]
    public async Task A_removed_snapshot_is_part_of_no_profile_in_History_the_row_count_or_the_delete_warning()
    {
        using var world = await RemovedWorld.CreateAsync();

        await world.ContinueFromFirstAsync();
        var history = (await world.Workspace.History.GetAsync(world.Game.Id, null, world.Workspace.Token)).Value!;
        var stats = (await world.Workspace.Profiles.GetRowStatsAsync(world.Game.Id, world.Workspace.Token)).Value!.Single();
        var preview = (await world.Workspace.Profiles.GetDeletePreviewAsync(world.Game.Id, world.Profile.Id, world.Workspace.Token)).Value!;

        Assert.Equal(["Vanilla"], history.Points.Select(point => point.Name));
        Assert.Equal(1, stats.SnapshotCount);
        Assert.Equal(1, preview.PointsOnlyHere);
    }

    [Fact(Timeout = 120_000)]
    public async Task A_removed_snapshot_names_no_profile_once_the_profile_it_left_is_deleted_and_the_automatic_cleanup_that_follows_keeps_it()
    {
        using var world = await RemovedWorld.CreateAsync();
        await world.ContinueFromFirstAsync();
        await world.Workspace.CreateProfileAsync(world.Game, "Other");
        await world.Workspace.WaitForIdleAsync();

        var deleted = await world.Workspace.Profiles.DeleteAsync(world.Game.Id, world.Profile.Id);
        await world.Workspace.WaitForIdleAsync();
        var removed = await world.ListAsync();

        Assert.True(deleted.Ok, deleted.Error?.Detail);
        Assert.Equal([world.SavesOnly, world.InstallOnly], removed.Select(snapshot => snapshot.Id));
        Assert.All(removed, snapshot =>
        {
            Assert.Null(snapshot.FromProfileId);
            Assert.Null(snapshot.FromProfileName);
        });
    }

    [Fact(Timeout = 120_000)]
    public async Task A_game_that_is_not_in_the_library_fails_with_the_game_not_found_error()
    {
        using var world = await RemovedWorld.CreateAsync();

        var result = await world.Workspace.History.GetRecentlyRemovedAsync("no-such-game", world.Workspace.Token);

        Assert.False(result.Ok);
        Assert.Equal(new GameNotFoundException("no-such-game").MessageId, result.Error.MessageId);
    }
}
