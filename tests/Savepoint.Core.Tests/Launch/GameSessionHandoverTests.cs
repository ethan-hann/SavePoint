using Savepoint.Core.Launch;
using Savepoint.Core.Platform;

namespace Savepoint.Core.Tests.Launch;

// On Linux a game whose starter exited before the first look is handed to init, so no parent id names the starter. The session
// then links it by when it started and where its program lies.
public class GameSessionHandoverTests
{
    private const string _folder = "/games/foo";

    private static TimeSpan Seconds(double seconds) => TimeSpan.FromSeconds(seconds);

    // An orphan as Linux lists it: parent 1, started the given seconds after the session began.
    private static RunningProcess Orphan(WatcherRig rig, int pid, double afterStart, string name = "real", string? path = null, int parent = 1) =>
        new(pid, name, path ?? _folder + "/bin/" + name, ParentPid: parent, StartedAt: rig.Watcher.StartedAt + Seconds(afterStart));

    private static async Task<WatcherRig> GoneStarterAsync()
    {
        var rig = await WatcherRig.ProgramAsync("Game.exe", OsKind.Linux, _folder);
        await rig.ExitChildAsync();
        return rig;
    }

    [Fact]
    public async Task A_lone_game_process_in_the_folder_that_started_just_after_a_starter_that_is_gone_is_followed_to_its_exit()
    {
        using var rig = await GoneStarterAsync();
        rig.Processes.Set(Orphan(rig, 600, 1), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(TimeSpan.FromMinutes(3));
        Assert.False(rig.Watcher.IsSettled);
        Assert.True(rig.Watcher.IsRunning());

        rig.Processes.Set(ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(4));
        var outcome = await rig.OutcomeAsync();

        Assert.Equal(SessionEndKind.NamedProcessEnded, outcome.Kind);
        Assert.Equal([600], outcome.FollowedPids);
    }

    [Fact]
    public async Task Two_such_processes_are_followed_by_neither_and_the_outcome_names_them()
    {
        using var rig = await GoneStarterAsync();
        rig.Processes.Set(Orphan(rig, 600, 1), Orphan(rig, 601, 2, "server"), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(WatcherRig.Grace);
        var outcome = await rig.OutcomeAsync();

        Assert.Equal(SessionEndKind.NamedProcessNeverAppeared, outcome.Kind);
        Assert.Empty(outcome.FollowedPids);
        Assert.Equal([600, 601], outcome.AmbiguousHandoverPids);
    }

    [Fact]
    public async Task A_second_such_process_that_appears_later_is_not_taken_for_the_game_once_one_is_followed()
    {
        using var rig = await GoneStarterAsync();
        rig.Processes.Set(Orphan(rig, 600, 1), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(4));
        rig.Processes.Set(Orphan(rig, 600, 1), Orphan(rig, 601, 6, "server"), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(4));

        rig.Processes.Set(Orphan(rig, 601, 6, "server"), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(WatcherRig.Grace + Seconds(4));
        var outcome = await rig.OutcomeAsync();

        Assert.Equal(SessionEndKind.NamedProcessNeverAppeared, outcome.Kind);
        Assert.Equal([600], outcome.FollowedPids);
    }

    [Fact]
    public async Task A_process_that_started_after_the_relaunch_window_is_not_the_starters_game()
    {
        using var rig = await GoneStarterAsync();
        rig.Processes.Set(Orphan(rig, 600, 45), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(WatcherRig.Grace);

        Assert.Equal(SessionEndKind.NamedProcessNeverAppeared, (await rig.OutcomeAsync()).Kind);
    }

    [Fact]
    public async Task A_process_that_started_before_the_session_is_not_the_starters_game()
    {
        using var rig = await GoneStarterAsync();
        rig.Processes.Set(Orphan(rig, 600, -60), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(WatcherRig.Grace);

        Assert.Equal(SessionEndKind.NamedProcessNeverAppeared, (await rig.OutcomeAsync()).Kind);
    }

    [Fact]
    public async Task A_process_outside_the_game_folder_is_not_the_starters_game_and_does_not_make_the_match_ambiguous()
    {
        using var rig = await GoneStarterAsync();
        rig.Processes.Set(Orphan(rig, 600, 1), Orphan(rig, 601, 1, "other", "/usr/bin/other"), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(TimeSpan.FromMinutes(3));
        Assert.False(rig.Watcher.IsSettled);

        rig.Processes.Set(ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(4));
        var outcome = await rig.OutcomeAsync();

        Assert.Equal([600], outcome.FollowedPids);
        Assert.Empty(outcome.AmbiguousHandoverPids);
    }

    [Fact]
    public async Task A_crash_reporter_in_the_game_folder_is_not_the_starters_game()
    {
        using var rig = await GoneStarterAsync();
        rig.Processes.Set(Orphan(rig, 600, 1, "crashpad_handler"), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(WatcherRig.Grace);

        Assert.Equal(SessionEndKind.NamedProcessNeverAppeared, (await rig.OutcomeAsync()).Kind);
    }

    [Fact]
    public async Task A_crash_reporter_beside_the_game_does_not_make_the_match_ambiguous()
    {
        using var rig = await GoneStarterAsync();
        rig.Processes.Set(Orphan(rig, 600, 1), Orphan(rig, 601, 1, "crashpad_handler"), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(TimeSpan.FromMinutes(3));
        Assert.False(rig.Watcher.IsSettled);

        rig.Processes.Set(ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(4));
        var outcome = await rig.OutcomeAsync();

        Assert.Equal([600], outcome.FollowedPids);
        Assert.Empty(outcome.AmbiguousHandoverPids);
    }

    [Fact]
    public async Task A_crash_reporter_listed_by_its_truncated_kernel_name_does_not_make_the_match_ambiguous()
    {
        using var rig = await GoneStarterAsync();
        rig.Processes.Set(Orphan(rig, 600, 1), Orphan(rig, 601, 1, "crashpad_handle", _folder + "/bin/crashpad_handler"), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(TimeSpan.FromMinutes(3));
        Assert.False(rig.Watcher.IsSettled);

        rig.Processes.Set(ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(4));
        var outcome = await rig.OutcomeAsync();

        Assert.Equal([600], outcome.FollowedPids);
        Assert.Empty(outcome.AmbiguousHandoverPids);
    }

    [Fact]
    public async Task A_process_that_started_just_before_the_session_is_not_taken_when_the_starter_was_never_listed()
    {
        using var rig = await GoneStarterAsync();
        rig.Processes.Set(Orphan(rig, 600, -5), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(WatcherRig.Grace);

        Assert.Equal(SessionEndKind.NamedProcessNeverAppeared, (await rig.OutcomeAsync()).Kind);
    }

    [Fact]
    public async Task A_child_of_a_process_that_started_during_the_session_is_not_an_orphan()
    {
        using var rig = await GoneStarterAsync();
        var helper = Orphan(rig, 500, 1, "helper", "/opt/store/helper");
        rig.Processes.Set(helper, Orphan(rig, 600, 2, parent: 500), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(WatcherRig.Grace);

        Assert.Equal(SessionEndKind.NamedProcessNeverAppeared, (await rig.OutcomeAsync()).Kind);
    }

    [Fact]
    public async Task The_children_of_a_linked_game_are_followed_with_it()
    {
        using var rig = await GoneStarterAsync();
        rig.Processes.Set(Orphan(rig, 600, 1), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(4));
        rig.Processes.Set(Orphan(rig, 600, 1), Orphan(rig, 601, 3, "worker", parent: 600), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(4));

        rig.Processes.Set(Orphan(rig, 601, 3, "worker", parent: 1), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(TimeSpan.FromMinutes(3));
        Assert.False(rig.Watcher.IsSettled);

        rig.Processes.Set(ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(4));

        Assert.Equal([600, 601], (await rig.OutcomeAsync()).FollowedPids);
    }

    [Fact]
    public async Task Windows_keeps_to_parent_ids_and_does_not_link_by_time_and_folder()
    {
        using var windows = await WatcherRig.ProgramAsync("Game.exe", OsKind.Windows, @"C:\games\foo");
        await windows.ExitChildAsync();
        windows.Processes.Set(new RunningProcess(600, "real", @"C:\games\foo\real.exe", ParentPid: 1, StartedAt: windows.Watcher.StartedAt + Seconds(1)), ScriptedInspector.Bystander);
        await windows.AdvanceAsync(WatcherRig.Grace);

        Assert.Equal(SessionEndKind.NamedProcessNeverAppeared, (await windows.OutcomeAsync()).Kind);
    }

    [Fact]
    public async Task A_linked_game_is_not_the_name_being_seen_so_the_session_ends_at_its_exit_after_the_grace()
    {
        using var rig = await GoneStarterAsync();
        rig.Processes.Set(Orphan(rig, 600, 1), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(WatcherRig.Grace + Seconds(10));
        Assert.False(rig.Watcher.IsSettled);

        rig.Processes.Set(ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(4));
        var outcome = await rig.OutcomeAsync();

        Assert.Equal(SessionEndKind.NamedProcessEnded, outcome.Kind);
        Assert.True(outcome.PlayDuration >= WatcherRig.Grace + Seconds(10));
    }
}
