using System.Diagnostics;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Launch;
using Savepoint.Core.Platform;

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
        // The guard is two polls, so a save asked soon after the quit waits out the rest of it. Two seconds apart is far more than the
        // few milliseconds one check takes between its own looks, and the test can tell the checks apart.
        var session = new GameSessionOptions { PollInterval = TimeSpan.FromSeconds(2), Grace = TimeSpan.FromSeconds(3), RelaunchWindow = TimeSpan.Zero };
        var processes = new RelaunchingInspector(FakeGameInstall.Inspector());
        var starter = new GameStarter(GameLaunchers.ForCurrentOs(), processes, PlatformFactory.CurrentOs, session, TimeProvider.System);
        using var rig = await LaunchRig.CreateAsync(starter: starter, followProcessName: true);
        processes.GameName = rig.ExeName;
        var vanilla = await rig.ProfileAsync("Vanilla");
        await rig.StartAsync(Tip(vanilla));
        await rig.WaitForGameAsync();
        var capturesBefore = CaptureJobs(rig);

        rig.KillGame();
        await rig.WaitForAsync<SessionState.Exited>();
        processes.Arm();
        var refused = await rig.Launch.CompleteSessionAsync(rig.Game.Id, new SessionCompletion.CaptureNew("Between runs"));

        Assert.False(refused.Ok);
        Assert.Equal("error.launch.sessionStillRunning", refused.Error?.MessageId);
        Assert.True(processes.Checks >= 2, "the save looked only once and trusted a list that had no game in it");
        Assert.Equal(capturesBefore, CaptureJobs(rig));
    }

    // The process list as a store's relaunch looks to the save: no game at the first check, a game at every check after it. The looks of
    // one check come within milliseconds of each other, so a look after a pause belongs to the next check.
    private sealed class RelaunchingInspector(IProcessInspector real) : IProcessInspector
    {
        private static readonly TimeSpan _sameCheck = TimeSpan.FromMilliseconds(500);
        private readonly object _gate = new();
        private long _lastLook;
        private int _checks;
        private bool _armed;

        public string GameName { get; set; } = string.Empty;

        public int Checks
        {
            get
            {
                lock (_gate)
                {
                    return _checks;
                }
            }
        }

        public void Arm()
        {
            lock (_gate)
            {
                _armed = true;
            }
        }

        public IReadOnlyList<RunningProcess> List()
        {
            var listed = real.List();
            lock (_gate)
            {
                if (!_armed)
                {
                    return listed;
                }

                var now = Stopwatch.GetTimestamp();
                if (_checks == 0 || Stopwatch.GetElapsedTime(_lastLook, now) > _sameCheck)
                {
                    _checks++;
                }

                _lastLook = now;
                return _checks < 2 ? [.. listed.Where(process => !ProcessNameMatcher.Matches(GameName, process, PlatformFactory.CurrentOs))] : [.. listed, new RunningProcess(4242, GameName, null)];
            }
        }
    }
}
