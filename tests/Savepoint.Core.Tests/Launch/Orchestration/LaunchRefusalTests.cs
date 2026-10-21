using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// What stops a launch before anything is switched (D13.8): unsaved work, an unfinished switch to somewhere else, a folder that moved.
public class LaunchRefusalTests
{
    private static LaunchTarget Tip(Profile profile) => new LaunchTarget.ProfileTip(profile.Id);

    // One launch that is expected to be refused: the job fails with this message and role, and the session never reaches the switch.
    private static async Task AssertRefusedAsync(LaunchRig rig, LaunchTarget target, string messageId, string role)
    {
        var before = rig.Events.Count;
        var job = await rig.StartAsync(target);

        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Equal(messageId, job.Error?.MessageId);
        Assert.Equal(role, job.Error!.Args["role"]);
        await rig.WaitForEventAsync(session => session.LaunchJobId == job.JobId && session.State is SessionState.Failed, "Failed for this launch");
        Assert.Equal(["Preparing", "Failed"], rig.Events.Skip(before).Select(session => session.State.GetType().Name).Distinct());
    }

    [Fact(Timeout = 240_000)]
    public async Task Unsaved_work_or_an_unfinished_switch_elsewhere_refuses_the_launch_and_nothing_is_switched()
    {
        var (rig, vanilla, modded) = await LaunchRig.WithTwoProfilesAsync();
        using var _ = rig;
        var installBefore = rig.SnapshotOf(RepoRole.Install);
        var savesBefore = rig.SnapshotOf(RepoRole.External);

        // The saves folder has unsaved work and the install folder is clean and could have been switched: it is not.
        rig.WriteSave("slot1.sav", "played on, never saved");
        await AssertRefusedAsync(rig, Tip(vanilla), "error.launch.dirtyWorkingTree", "external");
        Assert.Equal(installBefore, rig.SnapshotOf(RepoRole.Install));
        Assert.Equal(modded.BranchName, (await rig.StatusAsync(RepoRole.Install)).Branch);

        // With both folders dirty the install folder is named first.
        rig.WriteInstall("mods/c.txt", "an unsaved mod");
        await AssertRefusedAsync(rig, Tip(vanilla), "error.launch.dirtyWorkingTree", "install");
        File.Delete(Path.Combine(rig.InstallPath, "mods", "c.txt"));

        // A folder that already matches the target is still refused while it holds unsaved work.
        File.Delete(Path.Combine(rig.SavesPath, "slot1.sav"));
        rig.WriteSave("slot1.sav", "save one");
        rig.WriteSave("slot3.sav", "a new save nobody kept");
        await AssertRefusedAsync(rig, Tip(modded), "error.launch.dirtyWorkingTree", "external");
        File.Delete(Path.Combine(rig.SavesPath, "slot3.sav"));
        Assert.Equal(installBefore, rig.SnapshotOf(RepoRole.Install));
        Assert.Equal(savesBefore, rig.SnapshotOf(RepoRole.External));

        // A half-finished switch to another target cannot be told apart from work, so it refuses too, with its own error.
        var other = (await rig.CommitsAsync(RepoRole.Install, modded))[0].Id;
        var gitDir = await rig.WithEngineAsync((_, repo) => Task.FromResult(repo.GitDir), RepoRole.Install);
        SwitchJournal.Write(gitDir, (await rig.StatusAsync(RepoRole.Install)).HeadCommit, other);
        await AssertRefusedAsync(rig, Tip(vanilla), "error.launch.switchInterrupted", "install");
        SwitchJournal.Clear(gitDir);

        // A folder that has moved is named, and nothing is switched either.
        Directory.Delete(rig.SavesPath, recursive: true);
        await AssertRefusedAsync(rig, Tip(vanilla), "error.path.missing", "external");
        Assert.Equal(installBefore, rig.SnapshotOf(RepoRole.Install));
        Assert.Empty(rig.GameProcessIds());
    }
}
