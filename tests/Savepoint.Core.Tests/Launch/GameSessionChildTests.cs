using Savepoint.Core.Launch;
using Savepoint.Core.Platform;

namespace Savepoint.Core.Tests.Launch;

// A starter program that hands over to a process it starts itself, under a name SavePoint was not told: the session follows that child.
public class GameSessionChildTests
{
    private const string _name = "Stub";

    private static readonly DateTimeOffset _epoch = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static TimeSpan Seconds(double seconds) => TimeSpan.FromSeconds(seconds);

    private static DateTimeOffset At(double seconds) => _epoch + Seconds(seconds);

    // The started program as the process list shows it while it is alive.
    private static RunningProcess Stub(WatcherRig rig, double started = 0) => new(rig.Child!.Pid, _name, null, ParentPid: 1, StartedAt: At(started));

    private static RunningProcess Process(int pid, int parent, double started, string name = "Real") => new(pid, name, null, ParentPid: parent, StartedAt: At(started));

    [Fact]
    public async Task A_child_still_running_after_the_started_program_exits_keeps_the_session_open_until_it_exits()
    {
        using var rig = await WatcherRig.ProgramAsync(_name);
        rig.Processes.Set(Stub(rig), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(4));

        rig.Processes.Set(Process(600, rig.Child!.Pid, 3), ScriptedInspector.Bystander);
        await rig.ExitChildAsync();
        await rig.AdvanceAsync(TimeSpan.FromMinutes(5));
        Assert.False(rig.Watcher.IsSettled);

        rig.Processes.Set(ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(2));
        var outcome = await rig.OutcomeAsync();

        Assert.Equal(SessionEndKind.NamedProcessEnded, outcome.Kind);
        Assert.Equal(Seconds(4) + TimeSpan.FromMinutes(5) + Seconds(2), outcome.PlayDuration);
        Assert.Equal(0, outcome.ChildExitCode);
        Assert.Equal([600], outcome.FollowedPids);
    }

    [Fact]
    public async Task A_child_seen_while_the_started_program_was_alive_is_still_followed_after_it_leaves()
    {
        using var rig = await WatcherRig.ProgramAsync(_name);
        rig.Processes.Set(Stub(rig), Process(600, rig.Child!.Pid, 3), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(4));

        rig.Processes.Set(Process(600, rig.Child.Pid, 3), ScriptedInspector.Bystander);
        await rig.ExitChildAsync();
        await rig.AdvanceAsync(TimeSpan.FromMinutes(5));
        Assert.False(rig.Watcher.IsSettled);

        rig.Processes.Set(ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(2));

        Assert.Equal(SessionEndKind.NamedProcessEnded, (await rig.OutcomeAsync()).Kind);
    }

    [Fact]
    public async Task A_grandchild_is_followed_after_the_child_between_it_and_the_started_program_has_gone()
    {
        using var rig = await WatcherRig.ProgramAsync(_name);
        rig.Processes.Set(Stub(rig), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(2));
        rig.Processes.Set(Process(600, rig.Child!.Pid, 1, "Middle"), ScriptedInspector.Bystander);
        await rig.ExitChildAsync();
        await rig.AdvanceAsync(Seconds(4));

        rig.Processes.Set(Process(601, 600, 3), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(TimeSpan.FromMinutes(5));
        Assert.False(rig.Watcher.IsSettled);

        rig.Processes.Set(ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(2));

        Assert.Equal(SessionEndKind.NamedProcessEnded, (await rig.OutcomeAsync()).Kind);
    }

    [Fact]
    public async Task A_child_that_exits_before_the_started_program_leaves_nothing_to_follow()
    {
        using var rig = await WatcherRig.ProgramAsync("Game.exe");
        rig.Processes.Set(Stub(rig), Process(600, rig.Child!.Pid, 3), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(2));
        rig.Processes.Set(Stub(rig), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(2));

        rig.Processes.Set(ScriptedInspector.Bystander);
        await rig.ExitChildAsync();
        await rig.AdvanceAsync(WatcherRig.Grace);

        Assert.Equal(SessionEndKind.NamedProcessNeverAppeared, (await rig.OutcomeAsync()).Kind);
    }

    [Fact]
    public async Task A_process_the_started_program_did_not_start_is_not_followed()
    {
        using var rig = await WatcherRig.ProgramAsync("Game.exe");
        rig.Processes.Set(Stub(rig), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(2));

        rig.Processes.Set(Process(700, 1, 3, "Other"), Process(701, 700, 3, "OtherChild"), ScriptedInspector.Bystander);
        await rig.ExitChildAsync();
        await rig.AdvanceAsync(WatcherRig.Grace);

        Assert.Equal(SessionEndKind.NamedProcessNeverAppeared, (await rig.OutcomeAsync()).Kind);
    }

    [Fact]
    public async Task A_process_that_started_before_the_started_program_is_not_its_child_even_with_its_id_as_parent()
    {
        using var rig = await WatcherRig.ProgramAsync("Game.exe");
        rig.Processes.Set(Stub(rig, started: 10), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(2));

        rig.Processes.Set(Process(700, rig.Child!.Pid, 5, "Earlier"), ScriptedInspector.Bystander);
        await rig.ExitChildAsync();
        await rig.AdvanceAsync(WatcherRig.Grace);

        Assert.Equal(SessionEndKind.NamedProcessNeverAppeared, (await rig.OutcomeAsync()).Kind);
    }

    [Fact]
    public async Task A_new_process_that_took_over_the_id_of_the_started_program_and_its_children_are_not_followed()
    {
        using var rig = await WatcherRig.ProgramAsync("Game.exe");
        rig.Processes.Set(Stub(rig), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(2));

        var pid = rig.Child!.Pid;
        rig.Processes.Set(Process(pid, 1, 50, "Reuser"), Process(800, pid, 60, "ReuserChild"), ScriptedInspector.Bystander);
        await rig.ExitChildAsync();
        await rig.AdvanceAsync(WatcherRig.Grace);

        Assert.Equal(SessionEndKind.NamedProcessNeverAppeared, (await rig.OutcomeAsync()).Kind);
    }

    [Fact]
    public async Task A_child_of_the_started_program_is_still_followed_when_its_id_has_since_been_taken_by_another_process()
    {
        using var rig = await WatcherRig.ProgramAsync("Game.exe");
        rig.Processes.Set(Stub(rig), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(2));

        var pid = rig.Child!.Pid;
        rig.Processes.Set(Process(pid, 1, 50, "Reuser"), Process(600, pid, 3), ScriptedInspector.Bystander);
        await rig.ExitChildAsync();
        await rig.AdvanceAsync(TimeSpan.FromMinutes(5));
        Assert.False(rig.Watcher.IsSettled);

        rig.Processes.Set(Process(pid, 1, 50, "Reuser"), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(2));

        Assert.Equal(SessionEndKind.NamedProcessEnded, (await rig.OutcomeAsync()).Kind);
    }

    [Fact]
    public async Task A_process_list_without_parent_information_behaves_as_before()
    {
        using var rig = await WatcherRig.ProgramAsync("Game.exe");
        rig.Processes.Set(new RunningProcess(rig.Child!.Pid, _name, null), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(2));

        rig.Processes.Set(new RunningProcess(600, "Real", null), ScriptedInspector.Bystander);
        await rig.ExitChildAsync();
        await rig.AdvanceAsync(WatcherRig.Grace);

        Assert.Equal(SessionEndKind.NamedProcessNeverAppeared, (await rig.OutcomeAsync()).Kind);
    }

    [Fact]
    public async Task A_game_that_quits_to_be_started_again_by_a_store_it_started_ends_at_the_new_copys_exit_not_the_stores()
    {
        using var rig = await WatcherRig.ProgramAsync("Game.exe");
        rig.Processes.Set(new RunningProcess(rig.Child!.Pid, "Game", null, ParentPid: 1, StartedAt: At(0)), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(2));

        var store = Process(600, rig.Child.Pid, 1, "storeclient");
        rig.Processes.Set(store, ScriptedInspector.Bystander);
        await rig.ExitChildAsync();
        await rig.AdvanceAsync(Seconds(8));
        Assert.False(rig.Watcher.IsSettled);

        rig.Processes.Set(store, Process(900, 600, 11, "Game"), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(TimeSpan.FromMinutes(3));
        Assert.False(rig.Watcher.IsSettled);

        rig.Processes.Set(store, ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(2));
        var outcome = await rig.OutcomeAsync();

        Assert.Equal(SessionEndKind.NamedProcessEnded, outcome.Kind);
        Assert.Equal(Seconds(2 + 8) + TimeSpan.FromMinutes(3) + Seconds(2), outcome.PlayDuration);
    }

    [Fact]
    public async Task A_program_that_ran_a_while_and_exits_ends_the_session_even_though_something_it_started_lives_on()
    {
        using var rig = await WatcherRig.ProgramAsync("Game.exe");
        var game = new RunningProcess(rig.Child!.Pid, "Game", null, ParentPid: 1, StartedAt: At(0));
        rig.Processes.Set(game, Process(600, rig.Child.Pid, 5, "gamelauncher"), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(TimeSpan.FromMinutes(3));

        rig.Processes.Set(Process(600, rig.Child.Pid, 5, "gamelauncher"), ScriptedInspector.Bystander);
        await rig.ExitChildAsync();
        var outcome = await rig.OutcomeAsync();

        Assert.Equal(SessionEndKind.ChildExited, outcome.Kind);
        Assert.Equal(TimeSpan.FromMinutes(3), outcome.PlayDuration);
    }

    [Fact]
    public async Task A_link_session_does_not_follow_what_the_named_game_started()
    {
        using var rig = await WatcherRig.LinkAsync("Game.exe");
        rig.Processes.Set(new RunningProcess(700, "Game", null, ParentPid: 1, StartedAt: At(0)), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(2));
        rig.Processes.Set(new RunningProcess(700, "Game", null, ParentPid: 1, StartedAt: At(0)), Process(701, 700, 1, "gamelauncher"), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(2));

        rig.Processes.Set(Process(701, 700, 1, "gamelauncher"), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(2));

        Assert.Equal(SessionEndKind.NamedProcessEnded, (await rig.OutcomeAsync()).Kind);
    }

    [Theory]
    [InlineData("steamwebhelper")]
    [InlineData("Steam")]
    [InlineData("chrome")]
    [InlineData("WerFault")]
    public async Task A_known_helper_the_started_program_started_is_not_followed(string helper)
    {
        using var rig = await WatcherRig.ProgramAsync("Game.exe");
        rig.Processes.Set(Stub(rig), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(2));

        rig.Processes.Set(Process(600, rig.Child!.Pid, 1, helper), Process(601, 600, 2, "chrome"), ScriptedInspector.Bystander);
        await rig.ExitChildAsync();
        await rig.AdvanceAsync(WatcherRig.Grace);

        Assert.Equal(SessionEndKind.NamedProcessNeverAppeared, (await rig.OutcomeAsync()).Kind);
    }

    [Fact]
    public async Task A_followed_child_still_counts_as_running_after_the_player_says_they_are_done()
    {
        using var rig = await WatcherRig.ProgramAsync(_name);
        rig.Processes.Set(Stub(rig), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(2));
        rig.Processes.Set(Process(600, rig.Child!.Pid, 1), ScriptedInspector.Bystander);
        await rig.ExitChildAsync();
        await rig.AdvanceAsync(Seconds(4));
        Assert.True(await rig.FinishAsync());

        Assert.True(rig.Watcher.IsRunning());

        rig.Processes.Set(Process(600 + 1, rig.Child.Pid, 1), ScriptedInspector.Bystander);
        Assert.False(rig.Watcher.IsRunning());
    }

    [Fact]
    public async Task A_stale_parent_id_pointing_at_a_starter_that_was_never_listed_is_ignored_for_a_process_older_than_the_session()
    {
        using var rig = await WatcherRig.ProgramAsync("Game.exe");
        var older = rig.Watcher.StartedAt - TimeSpan.FromHours(1);
        var newer = rig.Watcher.StartedAt + Seconds(3);
        await rig.ExitChildAsync();

        rig.Processes.Set(new RunningProcess(700, "Old", null, ParentPid: rig.Child!.Pid, StartedAt: older), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(WatcherRig.Grace);

        Assert.Equal(SessionEndKind.NamedProcessNeverAppeared, (await rig.OutcomeAsync()).Kind);
        Assert.True(newer > older);
    }

    [Fact]
    public async Task A_child_newer_than_the_session_is_followed_when_the_starter_was_never_listed()
    {
        using var rig = await WatcherRig.ProgramAsync("Game.exe");
        var newer = rig.Watcher.StartedAt + Seconds(1);
        rig.Processes.Set(new RunningProcess(700, "Real", null, ParentPid: rig.Child!.Pid, StartedAt: newer), ScriptedInspector.Bystander);
        await rig.ExitChildAsync();
        await rig.AdvanceAsync(TimeSpan.FromMinutes(3));
        Assert.False(rig.Watcher.IsSettled);

        rig.Processes.Set(ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(2));

        Assert.Equal(SessionEndKind.NamedProcessEnded, (await rig.OutcomeAsync()).Kind);
    }

    [Fact]
    public async Task Following_a_child_does_not_start_the_grace_until_the_child_has_gone()
    {
        using var rig = await WatcherRig.ProgramAsync("Game.exe");
        rig.Processes.Set(Stub(rig), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(2));
        rig.Processes.Set(Process(600, rig.Child!.Pid, 1), ScriptedInspector.Bystander);
        await rig.ExitChildAsync();

        await rig.AdvanceAsync(TimeSpan.FromMinutes(10));

        Assert.False(rig.Watcher.IsSettled);
    }
}
