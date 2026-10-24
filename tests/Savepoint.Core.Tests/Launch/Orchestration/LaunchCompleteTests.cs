using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Tests.Services.Exe;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// What the player's choice after play does: a new point, an update of the latest one, and the capture job behind each.
public class LaunchCompleteTests
{
    private static readonly string[] _playsAndSaves = ["--save-every", "150", "--exit-after", "1500"];

    private static LaunchTarget Tip(Profile profile) => new LaunchTarget.ProfileTip(profile.Id);

    [Fact(Timeout = 240_000)]
    public async Task The_full_cycle_ends_with_the_session_saved_as_a_new_point_in_the_profile_that_was_played()
    {
        var (rig, vanilla, _) = await LaunchRig.WithTwoProfilesAsync(gameArguments: _playsAndSaves);
        using var _ = rig;
        await rig.StartAsync(Tip(vanilla));
        await rig.WaitForAsync<SessionState.Exited>();

        var completed = await rig.Launch.CompleteSessionAsync(rig.Game.Id, new SessionCompletion.CaptureNew("After play"));
        var done = Assert.IsType<SessionState.Completed>((await rig.WaitForAsync<SessionState.Completed>()).State);
        await rig.WaitForEventAsync(session => session.State is SessionState.Completed, "Completed");

        Assert.True(completed.Ok, completed.Error?.Detail);
        Assert.Equal(["Preparing", "Switching", "Running", "Exited", "Completing", "Completed"], rig.Phases());
        Assert.Equal(new SessionCompletion.CaptureNew("After play"), done.Completion);
        Assert.Null(done.Error);
        var result = Assert.IsType<CaptureResult>(done.Result);
        Assert.Equal(vanilla.Id, result.ProfileId);
        Assert.NotNull(result.PointId);
        Assert.Equal([RepoRole.Install, RepoRole.External], result.Folders.Select(folder => folder.Role).Order());
        Assert.All(result.Folders, folder => Assert.True(folder.Ok));

        // The capture ran as its own job, and the session pointed at it while it worked.
        var completing = rig.Events.Select(session => session.State).OfType<SessionState.Completing>().First();
        Assert.Equal(JobKind.Capture, rig.Jobs.Get(completing.JobId)?.Kind);
        Assert.Equal(JobStatus.Succeeded, rig.Jobs.Get(completing.JobId)?.Status);

        // History shows the new point, which holds the game's saves; the install did not change, so it has no new point.
        var history = (await rig.History.GetAsync(rig.Game.Id, null, rig.Token)).Value!;
        var point = history.Points[0];
        Assert.Equal(result.PointId, point.Id);
        Assert.Equal("After play", point.Name);
        Assert.Equal(HistoryPointKind.SaveOnly, point.Kind);
        Assert.Equal(vanilla.Id, point.ProfileId);
        Assert.True(point.LoadedNow);
        Assert.Equal(2, (await rig.CommitsAsync(RepoRole.External, vanilla)).Count);
        Assert.Single(await rig.CommitsAsync(RepoRole.Install, vanilla));
        var files = await rig.WithEngineAsync((engine, repo) => engine.ListTreeFilesAsync(repo, vanilla.BranchName, rig.Token), RepoRole.External);
        Assert.Contains(files, file => file.StartsWith("save-", StringComparison.Ordinal));
    }

    [Fact(Timeout = 180_000)]
    public async Task Updating_the_latest_point_rewrites_the_folders_that_changed_and_leaves_the_others_alone()
    {
        using var rig = await LaunchRig.CreateAsync(gameArguments: _playsAndSaves);
        var vanilla = await rig.ProfileAsync("Vanilla");
        var installBefore = (await rig.CommitsAsync(RepoRole.Install, vanilla))[0];
        var savesBefore = (await rig.CommitsAsync(RepoRole.External, vanilla))[0];
        await rig.StartAsync(Tip(vanilla));
        await rig.WaitForAsync<SessionState.Exited>();

        Assert.True((await rig.Launch.CompleteSessionAsync(rig.Game.Id, new SessionCompletion.UpdateExisting("Updated"))).Ok);
        var done = Assert.IsType<SessionState.Completed>((await rig.WaitForAsync<SessionState.Completed>()).State);

        Assert.Null(done.Error);
        Assert.All(Assert.IsType<CaptureResult>(done.Result).Folders, folder => Assert.True(folder.Ok));

        // The saves point was replaced in place: still one point, new name, new files.
        var saves = await rig.CommitsAsync(RepoRole.External, vanilla);
        Assert.Single(saves);
        Assert.Equal("Updated", saves[0].Subject);
        Assert.NotEqual(savesBefore.Id, saves[0].Id);
        var files = await rig.WithEngineAsync((engine, repo) => engine.ListTreeFilesAsync(repo, vanilla.BranchName, rig.Token), RepoRole.External);
        Assert.Contains(files, file => file.StartsWith("save-", StringComparison.Ordinal));

        // The install had nothing unsaved, so its point is exactly as it was: rewriting it would only have emptied it.
        var install = await rig.CommitsAsync(RepoRole.Install, vanilla);
        Assert.Equal(installBefore, Assert.Single(install));
    }

    [Fact(Timeout = 180_000)]
    public async Task Updating_the_latest_point_reports_the_folder_with_nothing_unsaved_as_skipped_and_the_other_as_saved()
    {
        using var rig = await LaunchRig.CreateAsync(gameArguments: _playsAndSaves);
        var vanilla = await rig.ProfileAsync("Vanilla");
        await rig.StartAsync(Tip(vanilla));
        await rig.WaitForAsync<SessionState.Exited>();

        Assert.True((await rig.Launch.CompleteSessionAsync(rig.Game.Id, new SessionCompletion.UpdateExisting("Updated"))).Ok);
        var done = Assert.IsType<SessionState.Completed>((await rig.WaitForAsync<SessionState.Completed>()).State);

        var folders = Assert.IsType<CaptureResult>(done.Result).Folders;
        var install = folders.Single(folder => folder.Role == RepoRole.Install);
        var saves = folders.Single(folder => folder.Role == RepoRole.External);
        Assert.True(install.Skipped, "the install had nothing unsaved and should say it was skipped");
        Assert.False(install.Saved);
        Assert.True(install.Ok);
        Assert.False(saves.Skipped);
        Assert.True(saves.Saved);
    }

    [Fact(Timeout = 180_000)]
    public async Task Updating_the_latest_point_logs_the_skipped_folder_as_skipped_and_not_as_done()
    {
        using var rig = await LaunchRig.CreateAsync(gameArguments: _playsAndSaves);
        var vanilla = await rig.ProfileAsync("Vanilla");
        await rig.StartAsync(Tip(vanilla));
        await rig.WaitForAsync<SessionState.Exited>();

        Assert.True((await rig.Launch.CompleteSessionAsync(rig.Game.Id, new SessionCompletion.UpdateExisting("Updated"))).Ok);
        await rig.WaitForAsync<SessionState.Completed>();

        var completing = rig.Events.Select(session => session.State).OfType<SessionState.Completing>().First();
        var steps = rig.Jobs.Get(completing.JobId)!.RecentLines.OfType<JobLine.Step>().ToList();
        Assert.Contains(JobLine.ForStep(JobSteps.CaptureFolderSkipped, ("folder", RepoRole.Install)), steps);
        Assert.DoesNotContain(steps, step => step.MessageId == JobSteps.CaptureFolderDone && Equals(step.Args["folder"], "install"));
        Assert.Contains(steps, step => step.MessageId == JobSteps.CaptureFolderDone && Equals(step.Args["folder"], "external"));
        Assert.DoesNotContain(steps, step => step.MessageId == JobSteps.CaptureFolderSkipped && Equals(step.Args["folder"], "external"));
    }

    [Fact(Timeout = 180_000)]
    public async Task A_capture_after_play_queues_the_routine_optimization_like_any_other_capture()
    {
        using var rig = await LaunchRig.CreateAsync(options => options with { LooseObjectThreshold = 0 }, _playsAndSaves);
        var vanilla = await rig.ProfileAsync("Vanilla");
        await rig.Workspace.WaitForIdleAsync(TimeSpan.FromSeconds(60));
        var compactions = 0;
        EventHandler<JobSnapshot> count = (_, job) =>
        {
            if (job.Kind == JobKind.Compact && job.Status == JobStatus.Running && job.Revision == 1)
            {
                Interlocked.Increment(ref compactions);
            }
        };
        rig.Jobs.JobChanged += count;
        try
        {
            await rig.StartAsync(Tip(vanilla));
            await rig.WaitForAsync<SessionState.Exited>();
            await rig.Launch.CompleteSessionAsync(rig.Game.Id, new SessionCompletion.CaptureNew("After play"));
            await rig.WaitForAsync<SessionState.Completed>();

            Assert.True(await FakeGameProcess.WaitUntilAsync(() => Volatile.Read(ref compactions) > 0), "no optimization was queued after the capture");
            await rig.Workspace.WaitForIdleAsync(TimeSpan.FromSeconds(60));
        }
        finally
        {
            rig.Jobs.JobChanged -= count;
        }
    }
}
