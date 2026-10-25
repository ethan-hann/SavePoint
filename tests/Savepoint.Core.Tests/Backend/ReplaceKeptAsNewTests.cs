using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Git;
using Savepoint.Core.Services.Profiles;

namespace Savepoint.Core.Tests.Backend;

// "Replace the snapshot with this session" rewrites a folder's commit of that snapshot only where it is the profile's newest and unshared.
// Where the profile has no commit of it, the files join as a new commit; where the commit is not the newest, the save is re-judged and the
// whole session becomes a new snapshot: nothing is lost, and the save says so instead of reading as a plain replacement.
public class ReplaceKeptAsNewTests
{
    [Fact(Timeout = 120_000)]
    public async Task A_folder_with_no_commit_of_the_replaced_snapshot_joins_it_while_the_other_folder_is_rewritten_and_nothing_is_kept_as_new()
    {
        using var world = await RemovedWorld.CreateAsync();
        BackendWorkspace.WriteFile(Path.Combine(world.Game.Install.Path, "mods", "b.esp"), "played");
        BackendWorkspace.WriteFile(Path.Combine(world.Game.External!.Path, "slot7.sav"), "played");
        var services = world.Workspace.Backend.Services;
        var game = (await services.Catalog.GetGameAsync(world.Game.Id, world.Workspace.Token))!;
        var capture = services.Capture.NewEvent();

        // The latest snapshot only recorded the saves folder, so the install folder has no commit of it to rewrite.
        var replace = new FolderCapturePlan(CaptureMode.Amend, SkipWhenClean: true, AmendPointId: world.SavesOnly.Value);
        var started = services.Capture.Start(game, world.Profile, "Replaced", [RepoRole.Install, RepoRole.External], capture, creating: false, new Dictionary<RepoRole, FolderCapturePlan> { [RepoRole.Install] = replace, [RepoRole.External] = replace });
        var job = await world.Workspace.WaitForJobAsync(started);

        var result = Assert.IsType<CaptureResult>(job.Result);
        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.False(result.Folders.Single(folder => folder.Role == RepoRole.Install).KeptAsNew);
        Assert.False(result.Folders.Single(folder => folder.Role == RepoRole.External).KeptAsNew);
        Assert.DoesNotContain(job.RecentLines, line => line is JobLine.Step { MessageId: JobSteps.CaptureFolderKeptAsNew });

        var install = await world.CommitsAsync(world.Profile.BranchName, RepoRole.Install);
        Assert.Equal(3, install.Count);
        Assert.Equal(capture.Id, install[0].CaptureId);
        var saves = await world.CommitsAsync(world.Profile.BranchName, RepoRole.External);
        Assert.Equal(2, saves.Count);
        Assert.Equal(capture.Id, saves[0].CaptureId);
    }

    [Fact(Timeout = 120_000)]
    public async Task A_replacement_whose_snapshot_is_not_the_newest_in_a_folder_saves_every_folder_as_a_new_snapshot_and_the_save_says_so()
    {
        using var world = await RemovedWorld.CreateAsync();
        BackendWorkspace.WriteFile(Path.Combine(world.Game.Install.Path, "mods", "b.esp"), "played");
        BackendWorkspace.WriteFile(Path.Combine(world.Game.External!.Path, "slot7.sav"), "played");
        var services = world.Workspace.Backend.Services;
        var game = (await services.Catalog.GetGameAsync(world.Game.Id, world.Workspace.Token))!;
        var capture = services.Capture.NewEvent();
        var installBefore = (await world.CommitsAsync(world.Profile.BranchName, RepoRole.Install)).Select(commit => commit.Id).ToList();
        var savesBefore = (await world.CommitsAsync(world.Profile.BranchName, RepoRole.External)).Select(commit => commit.Id).ToList();

        // The first snapshot has a newer one behind it in both folders, so rewriting it would drop history.
        var replace = new FolderCapturePlan(CaptureMode.Amend, SkipWhenClean: true, AmendPointId: world.First.Value);
        var started = services.Capture.Start(game, world.Profile, "Replaced", [RepoRole.Install, RepoRole.External], capture, creating: false, new Dictionary<RepoRole, FolderCapturePlan> { [RepoRole.Install] = replace, [RepoRole.External] = replace });
        var job = await world.Workspace.WaitForJobAsync(started);

        var result = Assert.IsType<CaptureResult>(job.Result);
        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.All(result.Folders, folder => Assert.True(folder.KeptAsNew));
        Assert.Contains(job.RecentLines, line => line is JobLine.Step { MessageId: JobSteps.CaptureFolderKeptAsNew });

        var install = await world.CommitsAsync(world.Profile.BranchName, RepoRole.Install);
        Assert.Equal(capture.Id, install[0].CaptureId);
        Assert.Equal(installBefore, install.Skip(1).Select(commit => commit.Id));
        var saves = await world.CommitsAsync(world.Profile.BranchName, RepoRole.External);
        Assert.Equal(capture.Id, saves[0].CaptureId);
        Assert.Equal(savesBefore, saves.Skip(1).Select(commit => commit.Id));
    }
}
