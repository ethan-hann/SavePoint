using Savepoint.Core.Launch;
using Savepoint.Core.Platform;

namespace Savepoint.Core.Tests.Launch;

// A store launcher's own helper process stays alive while it starts a game, so the grace for the game's name counts from when it was last seen.
public class GameSessionLauncherWaitTests
{
    private static TimeSpan Seconds(double seconds) => TimeSpan.FromSeconds(seconds);

    // The watcher is told by its caller what a helper is, so these tests give it their own answer.
    private static bool Busy(RunningProcess process) => process.Name == "reaper" && process.CommandLine!.Contains("AppId=240 ", StringComparison.Ordinal);

    private static RunningProcess Reaper(int appId = 240, int pid = 800) =>
        new(pid, "reaper", null, CommandLine: $"/x/reaper SteamLaunch AppId={appId} -- /x/entry-point --verb=waitforexitandrun");

    [Fact]
    public async Task The_grace_does_not_run_while_the_launcher_helper_is_listed_and_counts_from_when_it_was_last_seen()
    {
        using var rig = await WatcherRig.LinkAsync("Game.exe", OsKind.Linux, launcherBusy: Busy);
        rig.Processes.Set(Reaper(), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(TimeSpan.FromMinutes(3));
        Assert.False(rig.Watcher.IsSettled);

        rig.Processes.Set(ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(20));
        Assert.False(rig.Watcher.IsSettled);
        await rig.AdvanceAsync(Seconds(14));
        var outcome = await rig.OutcomeAsync();

        Assert.Equal(SessionEndKind.NamedProcessNeverAppeared, outcome.Kind);
        Assert.True(outcome.PlayDuration >= TimeSpan.FromMinutes(3) + WatcherRig.Grace, $"settled after only {outcome.PlayDuration}");
    }

    [Fact]
    public async Task A_launcher_helper_of_another_game_does_not_hold_the_grace()
    {
        using var rig = await WatcherRig.LinkAsync("Game.exe", OsKind.Linux, launcherBusy: Busy);
        rig.Processes.Set(Reaper(appId: 2400), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(WatcherRig.Grace);
        var outcome = await rig.OutcomeAsync();

        Assert.Equal(SessionEndKind.NamedProcessNeverAppeared, outcome.Kind);
        Assert.Equal(WatcherRig.Grace, outcome.PlayDuration);
    }

    [Fact]
    public async Task Without_a_launcher_matcher_the_grace_runs_as_before()
    {
        using var rig = await WatcherRig.LinkAsync("Game.exe", OsKind.Linux);
        rig.Processes.Set(Reaper(), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(WatcherRig.Grace);

        Assert.Equal(WatcherRig.Grace, (await rig.OutcomeAsync()).PlayDuration);
    }

    [Fact]
    public async Task A_gap_between_two_launcher_helpers_shorter_than_the_grace_does_not_end_the_wait()
    {
        using var rig = await WatcherRig.LinkAsync("Game.exe", OsKind.Linux, launcherBusy: Busy);
        rig.Processes.Set(Reaper(pid: 800), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(10));
        rig.Processes.Set(ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(4));

        rig.Processes.Set(Reaper(pid: 900), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(40));

        Assert.False(rig.Watcher.IsSettled);
    }

    [Fact]
    public async Task A_game_seen_and_then_gone_ends_the_session_at_once_even_while_the_launcher_helper_is_listed()
    {
        using var rig = await WatcherRig.LinkAsync("Game.exe", OsKind.Linux, launcherBusy: Busy);
        rig.Processes.Set(Reaper(), WatcherRig.Game, ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(6));
        Assert.False(rig.Watcher.IsSettled);

        rig.Processes.Set(Reaper(), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(4));

        Assert.Equal(SessionEndKind.NamedProcessEnded, (await rig.OutcomeAsync()).Kind);
    }

    [Fact]
    public async Task A_list_that_cannot_be_read_does_not_move_the_grace()
    {
        using var rig = await WatcherRig.LinkAsync("Game.exe", OsKind.Linux, launcherBusy: Busy);
        rig.Processes.Set(Reaper(), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(10));
        rig.Processes.Unreadable();
        await rig.AdvanceAsync(Seconds(32));
        var outcome = await rig.OutcomeAsync();

        Assert.True(outcome.PlayDuration < Seconds(50), $"settled after {outcome.PlayDuration}");
    }

    [Fact]
    public async Task A_program_start_is_not_held_by_a_launcher_helper()
    {
        using var rig = await WatcherRig.ProgramAsync("Game.exe", OsKind.Linux, launcherBusy: Busy);
        rig.Processes.Set(Reaper(), ScriptedInspector.Bystander);
        await rig.ExitChildAsync();
        await rig.AdvanceAsync(WatcherRig.Grace);

        Assert.Equal(SessionEndKind.NamedProcessNeverAppeared, (await rig.OutcomeAsync()).Kind);
    }
}
