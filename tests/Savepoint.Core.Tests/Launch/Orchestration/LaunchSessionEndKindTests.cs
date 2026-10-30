using Savepoint.Core.Api;
using Savepoint.Core.Launch;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// The exit says why the session ended, so a wrong process name can be told apart from a game that ran (D13.10, D48.7).
public class LaunchSessionEndKindTests
{
    [Fact(Timeout = 120_000)]
    public async Task A_process_name_that_never_appears_is_recorded_on_the_exit()
    {
        using var rig = await LaunchRig.CreateAsync(processName: "never-running-" + Guid.NewGuid().ToString("N")[..6], gameArguments: ["--exit-after", "300"]);
        var vanilla = await rig.ProfileAsync("Vanilla");

        await rig.StartAsync(new LaunchTarget.ProfileTip(vanilla.Id));
        var exited = Assert.IsType<SessionState.Exited>((await rig.WaitForAsync<SessionState.Exited>()).State);

        Assert.Equal(SessionEndKind.NamedProcessNeverAppeared, exited.EndKind);
    }

    [Fact(Timeout = 120_000)]
    public async Task A_program_that_exits_with_an_error_at_once_shows_its_code_while_waiting_and_ends_as_a_failed_start()
    {
        using var rig = await LaunchRig.CreateAsync(
            processName: "never-running-" + Guid.NewGuid().ToString("N")[..6],
            gameArguments: ["--exit-after", "300", "--exit-code", "127"]);
        var vanilla = await rig.ProfileAsync("Vanilla");

        await rig.StartAsync(new LaunchTarget.ProfileTip(vanilla.Id));
        var sawCode = false;
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTime.UtcNow < until && rig.Launch.GetSession(rig.Game.Id)?.State is not SessionState.Exited)
        {
            sawCode |= rig.Launch.GetSession(rig.Game.Id)?.State is SessionState.Running { ProgramExitCode: 127 };
            await Task.Delay(25, rig.Token);
        }

        var exited = Assert.IsType<SessionState.Exited>(rig.Launch.GetSession(rig.Game.Id)!.State);
        Assert.True(sawCode, "the running state never carried the exit code");
        Assert.Equal(SessionEndKind.ProgramFailedAtStart, exited.EndKind);
        Assert.Equal(127, exited.ProgramExitCode);
    }

    [Fact(Timeout = 120_000)]
    public async Task A_game_that_quits_on_its_own_after_the_relaunch_window_is_recorded_as_having_run()
    {
        // A zero window stands for a game that ran longer than it.
        var session = new GameSessionOptions { PollInterval = TimeSpan.FromMilliseconds(100), Grace = TimeSpan.FromSeconds(3), RelaunchWindow = TimeSpan.Zero };
        using var rig = await LaunchRig.CreateAsync(gameArguments: ["--exit-after", "300"], session: session);
        var vanilla = await rig.ProfileAsync("Vanilla");

        await rig.StartAsync(new LaunchTarget.ProfileTip(vanilla.Id));
        var exited = Assert.IsType<SessionState.Exited>((await rig.WaitForAsync<SessionState.Exited>()).State);

        Assert.Equal(SessionEndKind.ChildExited, exited.EndKind);
    }

    [Fact(Timeout = 120_000)]
    public async Task A_Steam_game_that_quits_cleanly_at_once_is_recorded_as_a_quit_at_start()
    {
        var (made, vanilla, _) = await LaunchRig.WithTwoProfilesAsync(gameArguments: ["--exit-after", "300"], steam: new FakeSteamClient(ready: true), steamLibrary: true);
        using var rig = made;

        await rig.StartAsync(new LaunchTarget.ProfileTip(vanilla.Id));
        var exited = Assert.IsType<SessionState.Exited>((await rig.WaitForAsync<SessionState.Exited>()).State);

        Assert.Equal(SessionEndKind.ProgramQuitAtStart, exited.EndKind);
    }

    [Fact(Timeout = 120_000)]
    public async Task A_game_outside_a_Steam_library_that_quits_cleanly_at_once_is_recorded_as_having_run()
    {
        using var rig = await LaunchRig.CreateAsync(gameArguments: ["--exit-after", "300"]);
        var vanilla = await rig.ProfileAsync("Vanilla");

        await rig.StartAsync(new LaunchTarget.ProfileTip(vanilla.Id));
        var exited = Assert.IsType<SessionState.Exited>((await rig.WaitForAsync<SessionState.Exited>()).State);

        Assert.Equal(SessionEndKind.ChildExited, exited.EndKind);
    }
}
