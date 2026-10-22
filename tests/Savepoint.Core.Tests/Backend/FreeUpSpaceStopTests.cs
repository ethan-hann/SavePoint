using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Git;
using Savepoint.Core.Services.Jobs;

namespace Savepoint.Core.Tests.Backend;

// Free up space treats a game's recently removed snapshots as one unit: every folder is judged before a record goes, the records of both
// folders go together or not at all, and once they are gone the run cannot be stopped, so it never ends with half a snapshot or with
// "nothing was lost" after something was.
public class FreeUpSpaceStopTests
{
    private const long _floor = 100L * 1024 * 1024;

    [Fact(Timeout = 240_000)]
    public async Task A_cancel_that_arrives_while_the_install_folder_is_repacked_is_refused_and_the_run_finishes_with_every_record_cleared()
    {
        var hooks = new GitHooks();
        using var world = await AfterContinueAsync(hooks);
        var preview = (await world.Workspace.Games.GetFreeUpSpacePreviewAsync(world.Game.Id, world.Workspace.Token)).Value!;
        Assert.Equal(3, preview.RecentlyRemovedSnapshots);
        var outcome = JobCancelOutcome.Unknown;
        hooks.OnGit = invocation =>
        {
            if (invocation.Arguments[0] == "repack" && invocation.Arguments.Contains("-a") && IsFolder(invocation, "install.git"))
            {
                outcome = world.Workspace.Backend.Services.Jobs.Cancel(RunningFreeUp(world));
            }
        };

        var started = await world.Workspace.Games.OptimizeStorageAsync(world.Game.Id, preview.Token);
        var job = await world.Workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(JobCancelOutcome.NotCancellable, outcome);
        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.False(job.Cancellable);
        Assert.False(job.CancelRequested);
        Assert.Empty(await world.ListAsync());
    }

    [Fact(Timeout = 240_000)]
    public async Task A_cancel_that_arrives_before_any_record_is_cleared_stops_the_run_and_every_snapshot_is_still_listed_whole()
    {
        var hooks = new GitHooks();
        using var world = await AfterContinueAsync(hooks);
        var before = await world.ListAsync();
        var preview = (await world.Workspace.Games.GetFreeUpSpacePreviewAsync(world.Game.Id, world.Workspace.Token)).Value!;

        // The saves folder is the second one judged, so the install folder has already passed its checks by then.
        hooks.OnGit = invocation =>
        {
            if (invocation.Arguments[0] == "count-objects" && IsFolder(invocation, "external.git"))
            {
                world.Workspace.Backend.Services.Jobs.Cancel(RunningFreeUp(world));
            }
        };
        var started = await world.Workspace.Games.OptimizeStorageAsync(world.Game.Id, preview.Token);
        var job = await world.Workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(JobStatus.Cancelled, job.Status);
        Assert.Equal("error.compaction.cancelled", job.Error!.MessageId);
        Assert.Equal(before.Select(snapshot => (snapshot.Id, snapshot.Kind)), (await world.ListAsync()).Select(snapshot => (snapshot.Id, snapshot.Kind)));
    }

    [Fact(Timeout = 240_000)]
    public async Task A_folder_the_volume_has_no_room_for_stops_the_run_before_any_folder_loses_a_record()
    {
        var hooks = new GitHooks();
        using var world = await AfterContinueAsync(hooks);
        var before = await world.ListAsync();
        var preview = (await world.Workspace.Games.GetFreeUpSpacePreviewAsync(world.Game.Id, world.Workspace.Token)).Value!;
        world.Workspace.Disk.FreePerPath = path => path.Contains("install.git", StringComparison.Ordinal) ? _floor / 2 : null;

        var started = await world.Workspace.Games.OptimizeStorageAsync(world.Game.Id, preview.Token);
        var job = await world.Workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Equal("error.diskSpace.low", job.Error!.MessageId);
        Assert.Equal(before.Select(snapshot => (snapshot.Id, snapshot.Kind)), (await world.ListAsync()).Select(snapshot => (snapshot.Id, snapshot.Kind)));
    }

    [Fact(Timeout = 240_000)]
    public async Task A_folder_that_cannot_clear_its_records_gives_back_the_ones_already_cleared_in_the_other_folder()
    {
        var hooks = new GitHooks();
        using var world = await AfterContinueAsync(hooks);
        var before = await world.ListAsync();
        var preview = (await world.Workspace.Games.GetFreeUpSpacePreviewAsync(world.Game.Id, world.Workspace.Token)).Value!;
        hooks.OnGit = invocation =>
        {
            if (invocation.Arguments[0] == "update-ref" && invocation.Arguments.Contains("savepoint: free up space") && IsFolder(invocation, "external.git"))
            {
                throw new IOException("The disk reported an error.");
            }
        };

        var started = await world.Workspace.Games.OptimizeStorageAsync(world.Game.Id, preview.Token);
        var job = await world.Workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Equal(before.Select(snapshot => (snapshot.Id, snapshot.Kind)), (await world.ListAsync()).Select(snapshot => (snapshot.Id, snapshot.Kind)));

        // The same run, started again once the disk answers, deletes them all.
        hooks.OnGit = null;
        var again = await world.Workspace.Games.OptimizeStorageAsync(world.Game.Id, preview.Token);
        Assert.Equal(JobStatus.Succeeded, (await world.Workspace.WaitForJobAsync(again.Value!.JobId)).Status);
        Assert.Empty(await world.ListAsync());
    }

    // A both-folder snapshot besides the two single-folder ones, so a run that clears one folder and not the other shows as a changed kind.
    private static async Task<RemovedWorld> AfterContinueAsync(GitHooks hooks)
    {
        var world = await RemovedWorld.CreateAsync(options => options with { Engine = new GitEngineOptions { InvocationObserver = hooks.Observe } });
        try
        {
            BackendWorkspace.WriteFile(Path.Combine(world.Game.Install.Path, "mods", "b.esp"), "another mod");
            BackendWorkspace.WriteFile(Path.Combine(world.Game.External!.Path, "slot3.sav"), "third save");
            await RemovedWorld.CaptureAsync(world.Workspace, world.Game, world.Profile, "Both again", RepoRole.Install, RepoRole.External);
            await world.ContinueFromFirstAsync();
            Assert.Equal(3, (await world.ListAsync()).Count);
            return world;
        }
        catch
        {
            world.Dispose();
            throw;
        }
    }

    private static bool IsFolder(GitInvocation invocation, string gitDirectoryName) =>
        invocation.GitDir is { } gitDir && string.Equals(Path.GetFileName(gitDir), gitDirectoryName, StringComparison.Ordinal);

    private static Guid RunningFreeUp(RemovedWorld world) =>
        world.Workspace.Jobs.List().Single(job => job.Kind == JobKind.Compact && job.Status == JobStatus.Running).JobId;

    // The engine sees every git call before it runs; a test points the hook at the call it wants to meet.
    private sealed class GitHooks
    {
        public Action<GitInvocation>? OnGit { get; set; }

        public void Observe(GitInvocation invocation) => OnGit?.Invoke(invocation);
    }
}
