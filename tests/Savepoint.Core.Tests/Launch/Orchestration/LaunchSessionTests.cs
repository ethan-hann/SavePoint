using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// The session for the life of the process (D13.4): nothing in the services' own life, a settings save or a storage move, drops it.
public class LaunchSessionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static LaunchTarget Tip(Profile profile) => new LaunchTarget.ProfileTip(profile.Id);

    [Fact(Timeout = 60_000)]
    public async Task A_game_with_no_session_has_nothing_to_finish_or_complete()
    {
        using var rig = await LaunchRig.CreateAsync();

        var finished = await rig.Launch.MarkFinishedAsync(rig.Game.Id);
        var completed = await rig.Launch.CompleteSessionAsync(rig.Game.Id, new SessionCompletion.Skip());

        Assert.Equal("error.launch.noActiveSession", finished.Error?.MessageId);
        Assert.Equal(rig.Game.Id, finished.Error!.Args["gameId"]);
        Assert.Equal("error.launch.noActiveSession", completed.Error?.MessageId);
        Assert.Null(rig.Launch.GetSession(rig.Game.Id));
        Assert.Empty(rig.Launch.ListSessions());
    }

    [Fact(Timeout = 180_000)]
    public async Task A_closed_game_ends_the_session_and_a_settings_save_and_a_storage_move_leave_it_alone()
    {
        var (rig, _, modded) = await LaunchRig.WithTwoProfilesAsync();
        using var _ = rig;
        await rig.Workspace.WaitForIdleAsync(TimeSpan.FromSeconds(60));
        var destination = Path.Combine(rig.Workspace.Root, "moved-storage");
        await rig.StartAsync(Tip(modded));
        await rig.WaitForGameAsync();

        // A settings save rebuilds nothing on the launch side.
        var before = rig.Launch.GetSession(rig.Game.Id)!;
        var settings = (await rig.Workspace.Settings.GetAsync(Ct)).Value!;
        Assert.True((await rig.Workspace.Settings.SaveAsync(settings with { Theme = ThemePreference.Dark })).Ok);
        Assert.Equal(before, rig.Launch.GetSession(rig.Game.Id));

        // Closing the game ends the session by itself.
        rig.KillGame();
        await rig.WaitForAsync<SessionState.Exited>();

        // Until the session is over a move is refused, which also shows the services see the launch side's sessions.
        var refused = await rig.Workspace.Settings.RelocateStorageAsync(destination);
        Assert.Equal("error.gameService.gameRunning", refused.Error?.MessageId);

        Assert.True((await rig.Launch.CompleteSessionAsync(rig.Game.Id, new SessionCompletion.Skip())).Ok);
        var completed = await rig.WaitForAsync<SessionState.Completed>();
        Assert.True(SpinWait.SpinUntil(() => rig.GameProcessIds().Count == 0, TimeSpan.FromSeconds(20)));
        var moved = await rig.Workspace.Settings.RelocateStorageAsync(destination);
        Assert.True(moved.Ok, moved.Error?.Detail);
        Assert.Equal(JobStatus.Succeeded, (await rig.WaitForJobAsync(moved.Value.JobId)).Status);

        // The finished session is still there, and the next launch reads the new root.
        Assert.Equal(completed, rig.Launch.GetSession(rig.Game.Id));
        File.Delete(rig.ReadyFile);
        var relaunched = await rig.StartAsync(Tip(modded));
        Assert.Equal(JobStatus.Succeeded, relaunched.Status);
        Assert.True(Directory.Exists(Path.Combine(destination, rig.Game.Id, "install.git")));
        Assert.False(Directory.Exists(Path.Combine(rig.Workspace.DefaultStorageRoot, rig.Game.Id)));
    }
}
