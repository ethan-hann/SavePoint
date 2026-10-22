using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Backend;

// A branch no profile names (what a killed Bring back or Duplicate leaves, or a profile the player did not reconnect) is no profile, so it
// never keeps a snapshot out of Recently removed. When it holds nothing but snapshots Free up space is deleting, it would also keep their
// space from being freed, so it goes with them; a branch that holds anything else is never touched.
public class FreeUpSpaceStrayBranchTests
{
    private const string _leftBehind = "profile/left-behind-abcd";
    private const string _unreconnected = "profile/unreconnected-1234";

    [Fact(Timeout = 240_000)]
    public async Task A_leftover_branch_that_holds_only_a_removed_snapshot_goes_with_it_and_the_snapshot_s_space_is_freed()
    {
        using var world = await RemovedWorld.CreateAsync();
        await world.ContinueFromFirstAsync();
        var removedInstall = (await world.RecordedCommitsAsync(RepoRole.Install)).Single();
        await CreateBranchAsync(world, _leftBehind, removedInstall);
        var preview = (await world.Workspace.Games.GetFreeUpSpacePreviewAsync(world.Game.Id, world.Workspace.Token)).Value!;
        Assert.Equal(2, preview.RecentlyRemovedSnapshots);

        var started = await world.Workspace.Games.OptimizeStorageAsync(world.Game.Id, preview.Token);
        var job = await world.Workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Empty(await world.ListAsync());
        Assert.False(await world.ObjectExistsAsync(RepoRole.Install, removedInstall));
        Assert.Equal([world.Profile.BranchName], await BranchesAsync(world, RepoRole.Install));
    }

    [Fact(Timeout = 240_000)]
    public async Task A_branch_that_also_holds_a_snapshot_nobody_agreed_to_lose_is_left_alone()
    {
        using var world = await RemovedWorld.CreateAsync();
        var storage = await world.Workspace.Backend.Services.Storage.GetAsync(world.Workspace.Token);

        // A profile the player chose not to reconnect: its snapshot sits on a branch no row names.
        await storage.Engine.CaptureAsync(world.RepoOf(storage, RepoRole.Install), new CaptureRequest(_unreconnected, "Old playthrough", "old-capture") { Mode = CaptureMode.Root }, ct: world.Workspace.Token);
        var unreconnectedTip = (await world.BranchTipAsync(_unreconnected, RepoRole.Install))!;
        await world.ContinueFromFirstAsync();
        var removedInstall = (await world.RecordedCommitsAsync(RepoRole.Install)).Single();
        await CreateBranchAsync(world, _leftBehind, removedInstall);
        var preview = (await world.Workspace.Games.GetFreeUpSpacePreviewAsync(world.Game.Id, world.Workspace.Token)).Value!;

        var started = await world.Workspace.Games.OptimizeStorageAsync(world.Game.Id, preview.Token);
        var job = await world.Workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.False(await world.ObjectExistsAsync(RepoRole.Install, removedInstall));
        Assert.Equal(new[] { _unreconnected, world.Profile.BranchName }.Order(StringComparer.Ordinal), (await BranchesAsync(world, RepoRole.Install)).Order(StringComparer.Ordinal));
        Assert.Equal(unreconnectedTip, await world.BranchTipAsync(_unreconnected, RepoRole.Install));
        Assert.True(await world.ObjectExistsAsync(RepoRole.Install, unreconnectedTip));
    }

    [Fact(Timeout = 240_000)]
    public async Task A_run_never_deletes_a_branch_a_profile_names_whatever_its_token_says()
    {
        using var world = await RemovedWorld.CreateAsync();
        await world.ContinueFromFirstAsync();
        var token = new FreeUpSpaceToken([new FreeUpSpaceRecord(world.Game.Id, RepoRole.Install, "refs/heads/" + world.Profile.BranchName)]);

        var started = await world.Workspace.Games.OptimizeStorageAsync(world.Game.Id, token);
        var job = await world.Workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Equal([world.Profile.BranchName], await BranchesAsync(world, RepoRole.Install));
        Assert.Equal(2, (await world.ListAsync()).Count);
        Assert.NotEmpty(await world.CommitsAsync(world.Profile.BranchName, RepoRole.Install));
    }

    private static async Task CreateBranchAsync(RemovedWorld world, string branch, string commit)
    {
        var storage = await world.Workspace.Backend.Services.Storage.GetAsync(world.Workspace.Token);
        await storage.Engine.CreateBranchAsync(world.RepoOf(storage, RepoRole.Install), branch, commit, world.Workspace.Token);
    }

    private static async Task<IReadOnlyList<string>> BranchesAsync(RemovedWorld world, RepoRole role)
    {
        var storage = await world.Workspace.Backend.Services.Storage.GetAsync(world.Workspace.Token);
        return await storage.Engine.ListBranchesAsync(world.RepoOf(storage, role), world.Workspace.Token);
    }
}
