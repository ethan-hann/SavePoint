using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Backend;

// Free up space, for one game or for all, is the only thing that deletes recently removed snapshots: it clears them first and then reclaims
// the space as it always did. The count it gives beforehand is what the confirmation tells the player.
public class FreeUpSpaceRemovedTests
{
    [Fact(Timeout = 240_000)]
    public async Task Free_up_space_for_a_game_deletes_its_removed_snapshots_and_what_they_held_and_leaves_the_other_games_alone()
    {
        using var alpha = await RemovedWorld.CreateAsync();
        using var beta = await RemovedWorld.CreateAsync(shared: alpha.Workspace, name: "Beta");
        await alpha.ContinueFromFirstAsync();
        await beta.ContinueFromFirstAsync();
        var alphaInstall = (await alpha.RecordedCommitsAsync(RepoRole.Install)).Single();
        var alphaSaves = (await alpha.RecordedCommitsAsync(RepoRole.External)).Single();
        var betaInstall = (await beta.RecordedCommitsAsync(RepoRole.Install)).Single();
        var preview = await alpha.Workspace.Games.GetFreeUpSpacePreviewAsync(alpha.Game.Id, alpha.Workspace.Token);

        var started = await alpha.Workspace.Games.OptimizeStorageAsync(alpha.Game.Id, preview.Value!.Token);
        var job = await alpha.Workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Empty(await alpha.ListAsync());
        Assert.False(await alpha.ObjectExistsAsync(RepoRole.Install, alphaInstall));
        Assert.False(await alpha.ObjectExistsAsync(RepoRole.External, alphaSaves));
        Assert.Equal(2, (await beta.ListAsync()).Count);
        Assert.True(await beta.ObjectExistsAsync(RepoRole.Install, betaInstall));

        // What the profile still holds is never touched.
        var history = (await alpha.Workspace.History.GetAsync(alpha.Game.Id, null, alpha.Workspace.Token)).Value!;
        Assert.Equal(["Vanilla"], history.Points.Select(point => point.Name));
    }

    [Fact(Timeout = 240_000)]
    public async Task Free_up_space_for_every_game_deletes_every_games_removed_snapshots()
    {
        using var alpha = await RemovedWorld.CreateAsync();
        using var beta = await RemovedWorld.CreateAsync(shared: alpha.Workspace, name: "Beta");
        await alpha.ContinueFromFirstAsync();
        await beta.ContinueFromFirstAsync();
        var betaSaves = (await beta.RecordedCommitsAsync(RepoRole.External)).Single();
        var preview = await alpha.Workspace.Settings.GetFreeUpSpacePreviewAsync(alpha.Workspace.Token);

        var started = await alpha.Workspace.Settings.OptimizeAllStorageAsync(preview.Value!.Token);
        var job = await alpha.Workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Empty(await alpha.ListAsync());
        Assert.Empty(await beta.ListAsync());
        Assert.False(await beta.ObjectExistsAsync(RepoRole.External, betaSaves));
    }

    [Fact(Timeout = 240_000)]
    public async Task A_removal_that_lands_while_the_free_up_waits_for_the_game_survives_it_and_the_reclaim()
    {
        using var world = await RemovedWorld.CreateAsync();
        var preview = await world.Workspace.Games.GetFreeUpSpacePreviewAsync(world.Game.Id, world.Workspace.Token);
        Assert.Equal(0, preview.Value!.RecentlyRemovedSnapshots);

        // A capture holds the game's lock while it records what it removes; the free-up the player starts then queues behind it.
        var inside = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var capture = world.Workspace.Backend.Services.RunForGameAsync(
            world.Game.Id,
            async (_, _) =>
            {
                inside.SetResult();
                await release.Task;
                return 0;
            },
            world.Workspace.Token);
        await inside.Task;
        var started = await world.Workspace.Games.OptimizeStorageAsync(world.Game.Id, preview.Value.Token);
        await world.ContinueFromFirstAsync();
        var installRecord = (await world.RecordedCommitsAsync(RepoRole.Install)).Single();
        var savesRecord = (await world.RecordedCommitsAsync(RepoRole.External)).Single();
        release.SetResult();
        await capture;
        var job = await world.Workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Equal([world.SavesOnly, world.InstallOnly], (await world.ListAsync()).Select(snapshot => snapshot.Id));
        Assert.True(await world.ObjectExistsAsync(RepoRole.Install, installRecord));
        Assert.True(await world.ObjectExistsAsync(RepoRole.External, savesRecord));
    }

    // A snapshot is listed while one of its commits is off every profile's branch, so the preview takes the records of its other folder too,
    // although a profile still holds that commit. When that profile then takes the commit off, its folder records the commit again under a
    // new name the player was never told about; deleting the old record and keeping the new one would leave half of the snapshot.
    [Fact(Timeout = 240_000)]
    public async Task A_snapshot_one_of_whose_commits_is_removed_again_while_the_free_up_waits_is_kept_whole()
    {
        using var world = await RemovedWorld.CreateAsync();
        BackendWorkspace.WriteFile(Path.Combine(world.Game.Install.Path, "mods", "b.esp"), "another mod");
        BackendWorkspace.WriteFile(Path.Combine(world.Game.External!.Path, "slot3.sav"), "third save");
        var both = await RemovedWorld.CaptureAsync(world.Workspace, world.Game, world.Profile, "Both again", RepoRole.Install, RepoRole.External);
        var copy = (await world.Workspace.Profiles.DuplicateAsync(world.Game.Id, world.Profile.Id, "Copy")).Value!;
        await world.ContinueFromFirstAsync();
        var preview = (await world.Workspace.Games.GetFreeUpSpacePreviewAsync(world.Game.Id, world.Workspace.Token)).Value!;
        Assert.Equal(new[] { both.Value, world.SavesOnly.Value }.Order(StringComparer.Ordinal), (await world.ListAsync()).Select(snapshot => snapshot.Id.Value).Order(StringComparer.Ordinal));

        // The free-up queues behind a capture, and the copy is continued from the first snapshot in the meantime.
        var inside = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var capture = world.Workspace.Backend.Services.RunForGameAsync(
            world.Game.Id,
            async (_, _) =>
            {
                inside.SetResult();
                await release.Task;
                return 0;
            },
            world.Workspace.Token);
        await inside.Task;
        var started = await world.Workspace.Games.OptimizeStorageAsync(world.Game.Id, preview.Token);
        await ContinueInstallAsync(world, copy.BranchName);
        release.SetResult();
        await capture;
        var job = await world.Workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        var left = (await world.ListAsync()).ToDictionary(snapshot => snapshot.Id);
        Assert.Equal(HistoryPointKind.VersionModsAndSave, left[both].Kind);
        Assert.DoesNotContain(world.SavesOnly, left.Keys);
        Assert.Contains(job.RecentLines.OfType<JobLine.Step>(), step => step.MessageId == JobSteps.CompactRemovedKept);
    }

    [Fact(Timeout = 240_000)]
    public async Task A_free_up_deletes_the_snapshots_the_preview_counted_and_keeps_one_removed_after_it()
    {
        using var world = await RemovedWorld.CreateAsync();
        await world.ContinueFromFirstAsync();
        var preview = await world.Workspace.Games.GetFreeUpSpacePreviewAsync(world.Game.Id, world.Workspace.Token);
        Assert.Equal(2, preview.Value!.RecentlyRemovedSnapshots);

        // Replacing the snapshot the profile continued from takes that one off the profile too, after the player was told about the other two.
        await world.ReplaceLatestAsync("Replaced");
        var started = await world.Workspace.Games.OptimizeStorageAsync(world.Game.Id, preview.Value.Token);
        var job = await world.Workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        var left = Assert.Single(await world.ListAsync());
        Assert.Equal(world.First, left.Id);
        Assert.Equal(HistoryPointKind.VersionModsAndSave, left.Kind);
    }

    [Fact(Timeout = 240_000)]
    public async Task A_free_up_for_every_game_keeps_the_removals_a_game_gained_after_the_preview()
    {
        using var alpha = await RemovedWorld.CreateAsync();
        using var beta = await RemovedWorld.CreateAsync(shared: alpha.Workspace, name: "Beta");
        await alpha.ContinueFromFirstAsync();
        var preview = await alpha.Workspace.Settings.GetFreeUpSpacePreviewAsync(alpha.Workspace.Token);
        Assert.Equal(2, preview.Value!.RecentlyRemovedSnapshots);

        // The second game is played on while the first is being tidied, and its profile is continued from an older snapshot.
        await beta.ContinueFromFirstAsync();
        var started = await alpha.Workspace.Settings.OptimizeAllStorageAsync(preview.Value.Token);
        var job = await alpha.Workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Empty(await alpha.ListAsync());
        Assert.Equal(2, (await beta.ListAsync()).Count);
    }

    [Fact(Timeout = 240_000)]
    public async Task A_free_up_started_without_a_preview_deletes_no_removed_snapshot_and_still_succeeds()
    {
        using var world = await RemovedWorld.CreateAsync();
        await world.ContinueFromFirstAsync();
        var installRecord = (await world.RecordedCommitsAsync(RepoRole.Install)).Single();

        var started = await world.Workspace.Games.OptimizeStorageAsync(world.Game.Id);
        var job = await world.Workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Equal(2, (await world.ListAsync()).Count);
        Assert.True(await world.ObjectExistsAsync(RepoRole.Install, installRecord));
    }

    [Fact(Timeout = 240_000)]
    public async Task Deleting_a_game_removes_its_removed_snapshots_with_the_rest_and_leaves_the_other_game_s()
    {
        using var alpha = await RemovedWorld.CreateAsync();
        using var beta = await RemovedWorld.CreateAsync(shared: alpha.Workspace, name: "Beta");
        await alpha.ContinueFromFirstAsync();
        await beta.ContinueFromFirstAsync();

        var storage = await alpha.Workspace.Backend.Services.Storage.GetAsync(alpha.Workspace.Token);
        var alphaStore = storage.GameDirectory(alpha.Game.Id);
        var betaStore = storage.GameDirectory(beta.Game.Id);
        Assert.True(Directory.Exists(alphaStore));

        var deleted = await alpha.Workspace.Games.DeleteAsync(alpha.Game.Id);
        var gone = await alpha.Workspace.History.GetRecentlyRemovedAsync(alpha.Game.Id, alpha.Workspace.Token);

        Assert.True(deleted.Ok, deleted.Error?.Detail);
        Assert.False(gone.Ok);
        Assert.False(Directory.Exists(alphaStore));
        Assert.True(Directory.Exists(betaStore));
        Assert.Equal(2, (await beta.ListAsync()).Count);
    }

    [Fact(Timeout = 240_000)]
    public async Task The_preview_counts_the_removed_snapshots_free_up_space_would_delete_for_a_game_and_for_every_game()
    {
        using var alpha = await RemovedWorld.CreateAsync();
        using var beta = await RemovedWorld.CreateAsync(shared: alpha.Workspace, name: "Beta");
        var token = alpha.Workspace.Token;
        var noneRemoved = await alpha.Workspace.Games.GetFreeUpSpacePreviewAsync(alpha.Game.Id, token);
        await alpha.ContinueFromFirstAsync();
        await beta.ContinueFromFirstAsync();

        var forAlpha = await alpha.Workspace.Games.GetFreeUpSpacePreviewAsync(alpha.Game.Id, token);
        var forAll = await alpha.Workspace.Settings.GetFreeUpSpacePreviewAsync(token);

        Assert.Equal(0, noneRemoved.Value!.RecentlyRemovedSnapshots);

        // Two snapshots left each game's profile, one in each folder.
        Assert.Equal(2, forAlpha.Value!.RecentlyRemovedSnapshots);
        Assert.Equal(4, forAll.Value!.RecentlyRemovedSnapshots);
    }

    [Fact(Timeout = 240_000)]
    public async Task The_preview_of_an_unknown_game_fails_with_the_game_not_found_error()
    {
        using var world = await RemovedWorld.CreateAsync();

        var result = await world.Workspace.Games.GetFreeUpSpacePreviewAsync("no-such-game", world.Workspace.Token);

        Assert.False(result.Ok);
        Assert.Equal(new GameNotFoundException("no-such-game").MessageId, result.Error.MessageId);
    }

    // The install folder alone goes back to the first snapshot on the profile's branch, as a continue does: what it takes off is recorded.
    private static async Task ContinueInstallAsync(RemovedWorld world, string branch)
    {
        var storage = await world.Workspace.Backend.Services.Storage.GetAsync(world.Workspace.Token);
        var repo = world.RepoOf(storage, RepoRole.Install);
        var start = (await storage.Engine.ListCommitsAsync(repo, branch, ct: world.Workspace.Token)).Single(commit => commit.CaptureId == world.First.Value);
        await storage.Engine.CheckoutCommitAsync(repo, start.Id, ct: world.Workspace.Token);
        var request = new CaptureRequest(branch, "Continued", Guid.NewGuid().ToString("D")) { Mode = CaptureMode.MoveForward, ExpectedStartCommit = start.Id };
        await storage.Engine.CaptureAsync(repo, request, ct: world.Workspace.Token);
    }
}
