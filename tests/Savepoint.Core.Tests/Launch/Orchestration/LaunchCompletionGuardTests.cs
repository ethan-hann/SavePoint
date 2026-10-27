using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Launch;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// The after-play save must never run while the game is still open, or while it is being started again: it would store half of a session.
public class LaunchCompletionGuardTests
{
    private static LaunchTarget Tip(Profile profile) => new LaunchTarget.ProfileTip(profile.Id);

    private static int CaptureJobs(LaunchRig rig) => rig.Jobs.List().Count(job => job.Kind == JobKind.Capture);

    // The player said they were done while the game kept running: the session is Exited and the game is still there.
    private static async Task<Profile> ExitedWhileStillRunningAsync(LaunchRig rig)
    {
        var vanilla = await rig.ProfileAsync("Vanilla");
        await rig.StartAsync(Tip(vanilla));
        await rig.WaitForGameAsync();
        Assert.True((await rig.Launch.MarkFinishedAsync(rig.Game.Id)).Ok);
        await rig.WaitForAsync<SessionState.Exited>();
        Assert.NotEmpty(rig.GameProcessIds());
        return vanilla;
    }

    [Fact(Timeout = 240_000)]
    public async Task A_save_is_refused_while_the_game_is_still_running_and_stored_nothing()
    {
        using var rig = await LaunchRig.CreateAsync(followProcessName: true);
        await ExitedWhileStillRunningAsync(rig);
        var capturesBefore = CaptureJobs(rig);

        var refused = await rig.Launch.CompleteSessionAsync(rig.Game.Id, new SessionCompletion.CaptureNew("Too early"));

        Assert.False(refused.Ok);
        Assert.Equal("error.launch.sessionStillRunning", refused.Error?.MessageId);
        Assert.IsType<SessionState.Exited>(rig.Launch.GetSession(rig.Game.Id)?.State);
        Assert.Equal(capturesBefore, CaptureJobs(rig));
    }

    [Fact(Timeout = 240_000)]
    public async Task The_same_save_goes_through_once_the_game_has_closed()
    {
        using var rig = await LaunchRig.CreateAsync(followProcessName: true);
        await ExitedWhileStillRunningAsync(rig);
        Assert.False((await rig.Launch.CompleteSessionAsync(rig.Game.Id, new SessionCompletion.CaptureNew("Too early"))).Ok);

        rig.KillGame();
        Assert.True(SpinWait.SpinUntil(() => rig.GameProcessIds().Count == 0, TimeSpan.FromSeconds(10)));
        var saved = await rig.Launch.CompleteSessionAsync(rig.Game.Id, new SessionCompletion.CaptureNew("After play"));

        Assert.True(saved.Ok, saved.Error?.Detail);
        var done = Assert.IsType<SessionState.Completed>((await rig.WaitForAsync<SessionState.Completed>()).State);
        Assert.Null(done.Error);
    }

    [Fact(Timeout = 240_000)]
    public async Task Skipping_the_save_is_allowed_while_the_game_is_still_running()
    {
        using var rig = await LaunchRig.CreateAsync(followProcessName: true);
        await ExitedWhileStillRunningAsync(rig);

        var skipped = await rig.Launch.CompleteSessionAsync(rig.Game.Id, new SessionCompletion.Skip());

        Assert.True(skipped.Ok, skipped.Error?.Detail);
        Assert.IsType<SessionState.Completed>(rig.Launch.GetSession(rig.Game.Id)?.State);
    }

    [Fact(Timeout = 240_000)]
    public async Task A_game_started_again_right_after_it_quit_is_not_saved_over()
    {
        // A relaunch shows up at the next look at the process list, so the save waits that long after the game quit before it trusts the list.
        var session = new GameSessionOptions { PollInterval = TimeSpan.FromMilliseconds(400), Grace = TimeSpan.FromSeconds(3), RelaunchWindow = TimeSpan.Zero };
        using var rig = await LaunchRig.CreateAsync(session: session, followProcessName: true);
        var vanilla = await rig.ProfileAsync("Vanilla");
        await rig.StartAsync(Tip(vanilla));
        await rig.WaitForGameAsync();
        var capturesBefore = CaptureJobs(rig);

        rig.KillGame();
        await rig.WaitForAsync<SessionState.Exited>();
        var saving = rig.Launch.CompleteSessionAsync(rig.Game.Id, new SessionCompletion.CaptureNew("Between runs"));
        rig.StartAnotherCopyOfTheGame();
        var refused = await saving;

        Assert.False(refused.Ok);
        Assert.Equal("error.launch.sessionStillRunning", refused.Error?.MessageId);
        Assert.Equal(capturesBefore, CaptureJobs(rig));
    }
}
