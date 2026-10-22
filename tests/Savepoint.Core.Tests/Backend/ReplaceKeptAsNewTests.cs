using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Git;
using Savepoint.Core.Services.Profiles;

namespace Savepoint.Core.Tests.Backend;

// "Replace the snapshot with this session" rewrites a folder's newest commit, so it only runs where that commit is the snapshot being replaced.
// Where it is not, the session becomes a new snapshot there: nothing is lost, and the save says so instead of reading as a plain replacement.
public class ReplaceKeptAsNewTests
{
    [Fact(Timeout = 120_000)]
    public async Task A_folder_whose_newest_snapshot_is_not_the_one_replaced_keeps_the_session_as_a_new_snapshot_and_the_save_says_so()
    {
        using var world = await RemovedWorld.CreateAsync();
        BackendWorkspace.WriteFile(Path.Combine(world.Game.Install.Path, "mods", "b.esp"), "played");
        BackendWorkspace.WriteFile(Path.Combine(world.Game.External!.Path, "slot7.sav"), "played");
        var services = world.Workspace.Backend.Services;
        var game = (await services.Catalog.GetGameAsync(world.Game.Id, world.Workspace.Token))!;

        // The latest snapshot only recorded the saves folder, so the install folder's newest commit belongs to an earlier one.
        var replace = new FolderCapturePlan(CaptureMode.Amend, SkipWhenClean: true, AmendPointId: world.SavesOnly.Value);
        var started = services.Capture.Start(game, world.Profile, "Replaced", [RepoRole.Install, RepoRole.External], services.Capture.NewEvent(), creating: false, new Dictionary<RepoRole, FolderCapturePlan> { [RepoRole.Install] = replace, [RepoRole.External] = replace });
        var job = await world.Workspace.WaitForJobAsync(started);

        var result = Assert.IsType<CaptureResult>(job.Result);
        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.True(result.Folders.Single(folder => folder.Role == RepoRole.Install).KeptAsNew);
        Assert.False(result.Folders.Single(folder => folder.Role == RepoRole.External).KeptAsNew);
        Assert.Contains(job.RecentLines, line => line is JobLine.Step { MessageId: JobSteps.CaptureFolderKeptAsNew });
        Assert.Equal(3, (await world.CommitsAsync(world.Profile.BranchName, RepoRole.Install)).Count);
    }
}
