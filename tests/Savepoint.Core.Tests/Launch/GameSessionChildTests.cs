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
