using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Git;
using Savepoint.Core.Tests.Git.Engine;
using Savepoint.Core.Tests.Services.Exe;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// An after-play save that only half worked is tried again as the same choice and under the same capture event, so the session stays one point (D31).
public class LaunchRetryCompletionTests
{
    private static readonly string[] _playsBriefly = ["--save-every", "150", "--exit-after", "900"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static LaunchTarget Tip(Profile profile) => new LaunchTarget.ProfileTip(profile.Id);

    private static bool AllSaved(LaunchSessionInfo session) =>
        session.State is SessionState.Completed { Result: { } result } && result.Folders.All(folder => folder.Ok);

    // The player's own edit to a tracked save, then made unreadable: the saves folder cannot be saved, the install can. Another program
    // holds it open where Windows enforces that, and elsewhere the account may not read it.
    private static async Task<IDisposable> LockEditedSaveAsync(LaunchRig rig)
    {
        rig.WriteSave("slot1.sav", "edited, then held open");
        if (!OperatingSystem.IsWindows())
        {
            return HeldFiles.Hold(Path.Combine(rig.SavesPath, "slot1.sav"), HeldFileKind.Unreadable);
        }

        var ready = Path.Combine(rig.Workspace.Root, "locker-ready.txt");
        var locker = FakeGameProcess.Start("--lock", Path.Combine(rig.SavesPath, "slot1.sav"), "--ready-file", ready);
        await FakeGameProcess.WaitForReadyFileAsync(ready);
        return locker;
    }

    [Fact(Timeout = 200_000)]
    public async Task Retrying_an_update_that_half_worked_replaces_the_point_in_place_under_the_original_capture_id()
    {
        using var rig = await LaunchRig.CreateAsync(gameArguments: _playsBriefly);
        var vanilla = await rig.ProfileAsync("Vanilla");
        await rig.StartAsync(Tip(vanilla));
        await rig.WaitForAsync<SessionState.Exited>();
        var locker = await LockEditedSaveAsync(rig);
        try
        {
            Assert.True((await rig.Launch.CompleteSessionAsync(rig.Game.Id, new SessionCompletion.UpdateExisting("Updated"))).Ok);
            var partial = Assert.IsType<SessionState.Completed>((await rig.WaitForAsync<SessionState.Completed>()).State);
            var folders = Assert.IsType<CaptureResult>(partial.Result).Folders;
            Assert.True(folders.Single(folder => folder.Role == RepoRole.Install).Ok);
            Assert.False(folders.Single(folder => folder.Role == RepoRole.External).Ok);
        }
        finally
        {
            locker.Dispose();
        }

        var retried = await rig.Launch.RetryCompletionAsync(rig.Game.Id);
        Assert.True(retried.Ok, retried.Error?.Detail);
        var done = Assert.IsType<SessionState.Completed>((await rig.WaitForAsync(AllSaved, "all folders saved")).State);

        Assert.Null(done.Error);
        var saves = await rig.CommitsAsync(RepoRole.External, vanilla);
        var replaced = Assert.Single(saves);
        Assert.Equal("Updated", replaced.Subject);
        Assert.Equal(done.Result!.PointId?.Value, replaced.CaptureId);
        Assert.Equal(["Preparing", "Running", "Exited", "Completing", "Completed", "Completing", "Completed"], rig.Phases());
    }

    [Fact(Timeout = 250_000)]
    public async Task Retrying_a_move_forward_that_half_worked_moves_the_profile_on_in_one_point()
    {
        using var rig = await LaunchRig.CreateAsync(gameArguments: _playsBriefly);
        var vanilla = await rig.ProfileAsync("Vanilla");
        await Task.Delay(1100, Ct);
        rig.WriteSave("slot2.sav", "two");
        await rig.CaptureAsync(vanilla, "Second");
        await Task.Delay(1100, Ct);
        rig.WriteSave("slot3.sav", "three");
        await rig.CaptureAsync(vanilla, "Third");
        var second = (await rig.PreviewAsync(Tip(vanilla))).AvailableSaves[1];
        await rig.StartAsync(new LaunchTarget.Save(vanilla.Id, second.Id));
        await rig.WaitForAsync<SessionState.Exited>();
        var locker = await LockEditedSaveAsync(rig);
        try
        {
            Assert.True((await rig.Launch.CompleteSessionAsync(rig.Game.Id, new SessionCompletion.MoveForward("Moved on"))).Ok);
            var partial = Assert.IsType<SessionState.Completed>((await rig.WaitForAsync<SessionState.Completed>()).State);
            Assert.Contains(Assert.IsType<CaptureResult>(partial.Result).Folders, folder => !folder.Ok);
        }
        finally
        {
            locker.Dispose();
        }

        var retried = await rig.Launch.RetryCompletionAsync(rig.Game.Id);
        Assert.True(retried.Ok, retried.Error?.Detail);
        var done = Assert.IsType<SessionState.Completed>((await rig.WaitForAsync(AllSaved, "all folders saved")).State);

        Assert.Null(done.Error);
        Assert.Equal(["Moved on", "Second", "Vanilla"], (await rig.CommitsAsync(RepoRole.External, vanilla)).Select(commit => commit.Subject));
        var status = await rig.StatusAsync(RepoRole.External);
        Assert.Equal(vanilla.BranchName, status.Branch);
        Assert.False(status.IsDetached);
    }

    [Fact(Timeout = 200_000)]
    public async Task A_plain_save_of_the_folders_a_half_worked_save_missed_joins_its_capture_event()
    {
        using var rig = await LaunchRig.CreateAsync(gameArguments: _playsBriefly);
        var vanilla = await rig.ProfileAsync("Vanilla");
        await rig.StartAsync(Tip(vanilla));
        await rig.WaitForAsync<SessionState.Exited>();
        rig.WriteInstall("settings.ini", "written by the game");
        var locker = await LockEditedSaveAsync(rig);
        try
        {
            Assert.True((await rig.Launch.CompleteSessionAsync(rig.Game.Id, new SessionCompletion.CaptureNew("After play"))).Ok);
            await rig.WaitForAsync<SessionState.Completed>();
        }
        finally
        {
            locker.Dispose();
        }

        var retried = await rig.CaptureAsync(vanilla, "After play", RepoRole.External);

        Assert.Equal(JobStatus.Succeeded, retried.Status);
        var history = (await rig.History.GetAsync(rig.Game.Id, null, Ct)).Value!;
        var afterPlay = Assert.Single(history.Points, point => point.Name == "After play");
        Assert.Equal(HistoryPointKind.VersionModsAndSave, afterPlay.Kind);
    }
}
