using Savepoint.Core.Launch;
using Savepoint.Core.Platform;
using Savepoint.Core.Tests.Services;

namespace Savepoint.Core.Tests.Launch;

public class GameSessionWatcherTests
{
    private const string _name = "Game.exe";

    // A watcher that kept polling after the advance would look again within moments, and only waiting shows that it does not.
    private static readonly TimeSpan _noMoreLooks = TimeSpan.FromMilliseconds(50);

    private static TimeSpan Seconds(double seconds) => TimeSpan.FromSeconds(seconds);

    // ---- a program started by SavePoint, no process name ----

    [Fact]
    public async Task A_program_without_a_process_name_ends_when_it_exits_and_says_how_long_it_ran()
    {
        using var rig = await WatcherRig.ProgramAsync();
        await rig.AdvanceAsync(TimeSpan.FromHours(1));
        Assert.False(rig.Watcher.IsSettled);

        rig.Child!.Exit(3);
        var outcome = await rig.OutcomeAsync();

        Assert.Equal(SessionEndKind.ChildExited, outcome.Kind);
        Assert.Equal(3, outcome.ChildExitCode);
        Assert.Equal(TimeSpan.FromHours(1), outcome.PlayDuration);
        Assert.Equal(rig.Watcher.StartedAt, outcome.StartedAt);
    }

    [Fact]
    public async Task A_program_without_a_process_name_never_reads_the_process_list()
    {
        using var rig = await WatcherRig.ProgramAsync();
        await rig.AdvanceAsync(TimeSpan.FromMinutes(5));

        rig.Child!.Exit();
        await rig.OutcomeAsync();

        Assert.Equal(0, rig.Processes.Calls);
    }

    [Fact]
    public async Task A_program_that_left_before_the_watcher_started_ends_at_once()
    {
        var child = new ScriptedProcess();
        child.Exit(9);
        var processes = new ScriptedInspector();
        using var watcher = new GameSessionWatcher(child, null, processes, OsKind.Windows, new GameSessionOptions(), TimeProvider.System);

        var outcome = await watcher.Outcome.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(SessionEndKind.ChildExited, outcome.Kind);
        Assert.Equal(9, outcome.ChildExitCode);
    }

    // ---- a launcher stub: the program exits early and the named process takes over ----

    [Fact]
    public async Task A_stub_that_hands_over_keeps_the_session_open_until_the_named_process_has_come_and_gone()
    {
        using var rig = await WatcherRig.ProgramAsync(_name);
        await rig.ExitChildAsync();

        await rig.AdvanceAsync(Seconds(5));
        rig.Processes.Set(WatcherRig.Game, ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(4));
        Assert.False(rig.Watcher.IsSettled);

        rig.Processes.Set(ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(2));
        var outcome = await rig.OutcomeAsync();

        Assert.Equal(SessionEndKind.NamedProcessEnded, outcome.Kind);
        Assert.Equal(0, outcome.ChildExitCode);
        Assert.Equal(Seconds(10), outcome.PlayDuration);
    }

    [Fact]
    public async Task A_named_process_already_running_when_the_stub_exits_is_followed_to_its_end()
    {
        using var rig = await WatcherRig.ProgramAsync(_name);
        rig.Processes.Set(WatcherRig.Game, ScriptedInspector.Bystander);

        await rig.ExitChildAsync();
        await rig.AdvanceAsync(TimeSpan.FromMinutes(2));
        Assert.False(rig.Watcher.IsSettled);

        rig.Processes.Set(ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(2));

        Assert.Equal(SessionEndKind.NamedProcessEnded, (await rig.OutcomeAsync()).Kind);
    }

    [Fact]
    public async Task The_named_process_has_the_grace_period_after_the_stub_exits_to_appear()
    {
        using var rig = await WatcherRig.ProgramAsync(_name);
        await rig.ExitChildAsync();

        await rig.AdvanceAsync(Seconds(29));
        Assert.False(rig.Watcher.IsSettled);
        rig.Processes.Set(WatcherRig.Game, ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(2));
        Assert.False(rig.Watcher.IsSettled);

        rig.Processes.Set(ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(2));

        Assert.Equal(SessionEndKind.NamedProcessEnded, (await rig.OutcomeAsync()).Kind);
    }

    [Fact]
    public async Task A_named_process_that_never_appears_settles_the_session_when_the_grace_runs_out()
    {
        using var rig = await WatcherRig.ProgramAsync(_name);
        await rig.ExitChildAsync();

        await rig.AdvanceAsync(Seconds(29));
        Assert.False(rig.Watcher.IsSettled);
        await rig.AdvanceAsync(Seconds(1));
        var outcome = await rig.OutcomeAsync();

        Assert.Equal(SessionEndKind.NamedProcessNeverAppeared, outcome.Kind);
        Assert.Equal(Seconds(30), outcome.PlayDuration);
    }

    // ---- a program that fails right away ----

    [Fact]
    public async Task A_program_that_exits_with_an_error_at_once_and_is_never_followed_by_the_game_ends_as_a_failed_start_when_the_grace_runs_out()
    {
        using var rig = await WatcherRig.ProgramAsync(_name);
        await rig.AdvanceAsync(Seconds(1));

        await rig.ExitChildAsync(127);
        await rig.AdvanceAsync(Seconds(29));
        Assert.False(rig.Watcher.IsSettled);
        await rig.AdvanceAsync(Seconds(1));

        var outcome = await rig.OutcomeAsync();
        Assert.Equal(SessionEndKind.ProgramFailedAtStart, outcome.Kind);
        Assert.Equal(127, outcome.ChildExitCode);
        Assert.Equal(Seconds(1), outcome.PlayDuration);
    }

    [Fact]
    public async Task A_program_that_asks_Steam_to_start_it_again_and_exits_with_one_is_followed_to_the_new_copy()
    {
        using var rig = await WatcherRig.ProgramAsync(_name, options: new GameSessionOptions { PollInterval = WatcherRig.Interval, Grace = TimeSpan.FromMinutes(2) });
        await rig.AdvanceAsync(Seconds(1));

        await rig.ExitChildAsync(1);
        await rig.AdvanceAsync(Seconds(45));
        Assert.False(rig.Watcher.IsSettled);
        rig.Processes.Set(WatcherRig.Game, ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(10));
        Assert.False(rig.Watcher.IsSettled);

        rig.Processes.Set(ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(2));

        var outcome = await rig.OutcomeAsync();
        Assert.Equal(SessionEndKind.NamedProcessEnded, outcome.Kind);
    }

    [Fact]
    public async Task The_early_exit_code_shows_during_the_wait_and_clears_when_the_game_appears()
    {
        using var rig = await WatcherRig.ProgramAsync(_name);
        Assert.Null(rig.Watcher.EarlyExitCode);
        var changes = 0;
        rig.Watcher.EarlyExitChanged += (_, _) => Interlocked.Increment(ref changes);

        await rig.ExitChildAsync(127);
        await rig.AdvanceAsync(Seconds(4));
        Assert.Equal(127, rig.Watcher.EarlyExitCode);
        Assert.Equal(1, changes);

        rig.Processes.Set(WatcherRig.Game, ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(4));

        Assert.Null(rig.Watcher.EarlyExitCode);
        Assert.Equal(2, changes);
    }

    [Fact]
    public async Task A_program_that_exits_with_code_zero_has_no_early_exit_code()
    {
        using var rig = await WatcherRig.ProgramAsync(_name);

        await rig.ExitChildAsync(0);
        await rig.AdvanceAsync(Seconds(4));

        Assert.Null(rig.Watcher.EarlyExitCode);
    }

    [Fact]
    public async Task A_program_that_exits_with_code_zero_at_once_still_has_the_grace()
    {
        using var rig = await WatcherRig.ProgramAsync(_name);
        await rig.AdvanceAsync(Seconds(1));

        await rig.ExitChildAsync(0);
        await rig.AdvanceAsync(Seconds(10));

        Assert.False(rig.Watcher.IsSettled);
    }

    [Fact]
    public async Task A_program_that_ran_past_the_relaunch_window_before_exiting_with_an_error_is_not_a_failed_start()
    {
        using var rig = await WatcherRig.ProgramAsync(_name);
        await rig.AdvanceAsync(Seconds(40));

        await rig.ExitChildAsync(1);
        await rig.AdvanceAsync(WatcherRig.Grace);

        Assert.Equal(SessionEndKind.NamedProcessNeverMatched, (await rig.OutcomeAsync()).Kind);
    }

    [Fact]
    public async Task A_program_that_exits_with_an_error_while_the_named_process_runs_is_followed_to_its_end()
    {
        using var rig = await WatcherRig.ProgramAsync(_name);
        rig.Processes.Set(WatcherRig.Game, ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(2));

        await rig.ExitChildAsync(1);
        await rig.AdvanceAsync(Seconds(10));
        Assert.False(rig.Watcher.IsSettled);

        rig.Processes.Set(ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(2));

        Assert.Equal(SessionEndKind.NamedProcessEnded, (await rig.OutcomeAsync()).Kind);
    }

    [Fact]
    public async Task The_grace_is_counted_from_the_stubs_exit_and_not_while_the_program_is_alive()
    {
        using var rig = await WatcherRig.ProgramAsync(_name);
        await rig.AdvanceAsync(TimeSpan.FromMinutes(3));
        Assert.False(rig.Watcher.IsSettled);

        await rig.ExitChildAsync();
        await rig.AdvanceAsync(Seconds(29));
        Assert.False(rig.Watcher.IsSettled);
        await rig.AdvanceAsync(Seconds(1));
        var outcome = await rig.OutcomeAsync();

        Assert.Equal(SessionEndKind.NamedProcessNeverMatched, outcome.Kind);
        Assert.Equal(TimeSpan.FromMinutes(3), outcome.PlayDuration);
    }

    [Fact]
    public async Task A_program_that_ran_a_while_and_exited_is_still_given_the_grace_for_a_late_handover_before_its_run_is_called_play_time()
    {
        using var rig = await WatcherRig.ProgramAsync(_name);
        await rig.AdvanceAsync(Seconds(40));
        await rig.ExitChildAsync();

        await rig.AdvanceAsync(Seconds(10));
        rig.Processes.Set(WatcherRig.Game, ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(4));
        rig.Processes.Set(ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(2));

        Assert.Equal(SessionEndKind.NamedProcessEnded, (await rig.OutcomeAsync()).Kind);
    }

    [Fact]
    public async Task A_program_that_exited_within_the_relaunch_window_and_never_matched_is_never_seen()
    {
        using var rig = await WatcherRig.ProgramAsync(_name);
        await rig.AdvanceAsync(Seconds(10));
        await rig.ExitChildAsync();

        await rig.AdvanceAsync(WatcherRig.Grace);

        Assert.Equal(SessionEndKind.NamedProcessNeverAppeared, (await rig.OutcomeAsync()).Kind);
    }

    [Fact]
    public async Task A_list_that_could_not_be_read_for_the_grace_after_a_long_run_ends_at_the_programs_exit_without_blaming_the_name()
    {
        using var rig = await WatcherRig.ProgramAsync(_name);
        rig.Processes.Unreadable();
        await rig.AdvanceAsync(TimeSpan.FromMinutes(3));
        await rig.ExitChildAsync();

        await rig.AdvanceAsync(WatcherRig.Grace);
        var outcome = await rig.OutcomeAsync();

        Assert.Equal(SessionEndKind.ChildExited, outcome.Kind);
        Assert.Equal(TimeSpan.FromMinutes(3), outcome.PlayDuration);
    }

    [Fact]
    public async Task A_list_that_was_readable_at_any_look_during_the_grace_counts_as_a_name_that_never_appeared()
    {
        using var rig = await WatcherRig.ProgramAsync(_name);
        rig.Processes.Unreadable();
        await rig.ExitChildAsync();
        await rig.AdvanceAsync(Seconds(10));
        rig.Processes.Set(ScriptedInspector.Bystander);

        await rig.AdvanceAsync(Seconds(20));

        Assert.Equal(SessionEndKind.NamedProcessNeverAppeared, (await rig.OutcomeAsync()).Kind);
    }

    [Fact]
    public async Task A_process_seen_while_the_program_ran_ends_the_session_the_moment_the_program_exits_after_it()
    {
        using var rig = await WatcherRig.ProgramAsync(_name);
        rig.Processes.Set(WatcherRig.Game, ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(4));
        rig.Processes.Set(ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(10));
        Assert.False(rig.Watcher.IsSettled);

        await rig.ExitChildAsync(2);
        var outcome = await rig.OutcomeAsync();

        Assert.Equal(SessionEndKind.NamedProcessEnded, outcome.Kind);
        Assert.Equal(2, outcome.ChildExitCode);
        Assert.Equal(Seconds(14), outcome.PlayDuration);
    }

    [Fact]
    public async Task A_named_process_with_another_pid_that_is_gone_when_the_program_exits_ends_the_session_at_once()
    {
        using var rig = await WatcherRig.ProgramAsync(_name);
        rig.Processes.Set(WatcherRig.Game, ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(4));

        rig.Processes.Set(ScriptedInspector.Bystander);
        await rig.ExitChildAsync();
        var outcome = await rig.OutcomeAsync();

        Assert.Equal(SessionEndKind.NamedProcessEnded, outcome.Kind);
        Assert.Equal(Seconds(4), outcome.PlayDuration);
    }

    // A game that asks its store to start it again exits at once, and the store starts a new copy under the same name a moment later.
    private static RunningProcess Copy(int pid) => new(pid, "Game", null);

    [Fact]
    public async Task A_program_that_exits_with_an_error_and_is_started_again_under_its_own_name_is_followed_to_the_end_of_the_new_copy()
    {
        using var rig = await WatcherRig.ProgramAsync(_name);
        rig.Processes.Set(Copy(rig.Child!.Pid), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(4));

        rig.Processes.Set(ScriptedInspector.Bystander);
        await rig.ExitChildAsync(1);
        await rig.AdvanceAsync(Seconds(6));
        Assert.False(rig.Watcher.IsSettled);

        rig.Processes.Set(Copy(901), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(TimeSpan.FromMinutes(5));
        Assert.False(rig.Watcher.IsSettled);

        rig.Processes.Set(ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(2));
        var outcome = await rig.OutcomeAsync();

        Assert.Equal(SessionEndKind.NamedProcessEnded, outcome.Kind);
        Assert.Equal(Seconds(4 + 6) + TimeSpan.FromMinutes(5) + Seconds(2), outcome.PlayDuration);
        Assert.Equal(rig.Child.Pid, outcome.StartedPid);
        Assert.Equal([901], outcome.FollowedPids);
    }

    [Fact]
    public async Task A_program_that_ran_past_the_relaunch_window_and_exits_ends_the_session_at_once()
    {
        using var rig = await WatcherRig.ProgramAsync(_name);
        rig.Processes.Set(Copy(rig.Child!.Pid), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(40));

        rig.Processes.Set(ScriptedInspector.Bystander);
        await rig.ExitChildAsync();

        Assert.True(rig.Watcher.IsSettled, "a game that was quit after a long run must not get the grace");
        var outcome = await rig.OutcomeAsync();
        Assert.Equal(SessionEndKind.ChildExited, outcome.Kind);
        Assert.Equal(Seconds(40), outcome.PlayDuration);
    }

    [Fact]
    public async Task A_program_that_exits_at_exactly_the_relaunch_window_counts_as_quit()
    {
        using var rig = await WatcherRig.ProgramAsync(_name);
        rig.Processes.Set(Copy(rig.Child!.Pid), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(30));

        rig.Processes.Set(ScriptedInspector.Bystander);
        await rig.ExitChildAsync();

        Assert.True(rig.Watcher.IsSettled, "the window is closed at its own length");
        Assert.Equal(Seconds(30), (await rig.OutcomeAsync()).PlayDuration);
    }

    [Fact]
    public async Task A_program_that_exits_just_inside_the_relaunch_window_gets_the_grace()
    {
        using var rig = await WatcherRig.ProgramAsync(_name);
        rig.Processes.Set(Copy(rig.Child!.Pid), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(29));

        rig.Processes.Set(ScriptedInspector.Bystander);
        await rig.ExitChildAsync(1);
        await rig.AdvanceAsync(Seconds(20));

        Assert.False(rig.Watcher.IsSettled);
    }

    // A player who quits a game a few seconds in is not a store starting it again: that exits with an error, or never ran as the named game.
    [Fact]
    public async Task A_program_seen_under_its_name_that_quits_cleanly_inside_the_relaunch_window_ends_the_session_after_the_relaunch_guard()
    {
        using var rig = await WatcherRig.ProgramAsync(_name);
        rig.Processes.Set(Copy(rig.Child!.Pid), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(17));

        rig.Processes.Set(ScriptedInspector.Bystander);
        await rig.ExitChildAsync(0);
        await rig.AdvanceAsync(Seconds(2));
        Assert.False(rig.Watcher.IsSettled, "a copy started at once would be listed at the next look");
        await rig.AdvanceAsync(Seconds(2));

        Assert.True(rig.Watcher.IsSettled, "a clean quit waited out the whole grace");
        var outcome = await rig.OutcomeAsync();
        Assert.Equal(SessionEndKind.ChildExited, outcome.Kind);
        Assert.Equal(Seconds(17), outcome.PlayDuration);
        Assert.Equal(0, outcome.ChildExitCode);
    }

    [Fact]
    public async Task A_program_seen_under_its_name_that_quits_cleanly_and_is_started_again_within_the_guard_is_followed_to_the_new_copy()
    {
        using var rig = await WatcherRig.ProgramAsync(_name);
        rig.Processes.Set(Copy(rig.Child!.Pid), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(4));

        rig.Processes.Set(ScriptedInspector.Bystander);
        await rig.ExitChildAsync(0);
        rig.Processes.Set(Copy(901), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(12));
        Assert.False(rig.Watcher.IsSettled);

        rig.Processes.Set(ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(2));

        var outcome = await rig.OutcomeAsync();
        Assert.Equal(SessionEndKind.NamedProcessEnded, outcome.Kind);
        Assert.Equal([901], outcome.FollowedPids);
    }

    [Fact]
    public async Task A_program_seen_under_its_name_that_exits_with_an_error_inside_the_relaunch_window_keeps_the_whole_grace()
    {
        using var rig = await WatcherRig.ProgramAsync(_name, options: new GameSessionOptions { PollInterval = WatcherRig.Interval, Grace = TimeSpan.FromMinutes(2) });
        rig.Processes.Set(Copy(rig.Child!.Pid), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(4));

        rig.Processes.Set(ScriptedInspector.Bystander);
        await rig.ExitChildAsync(1);
        await rig.AdvanceAsync(Seconds(100));

        Assert.False(rig.Watcher.IsSettled, "a store may start the game again a minute after an error exit");
    }

    [Fact]
    public async Task A_slow_quit_is_not_settled_on_a_read_that_could_not_be_made_and_ends_at_the_exit_on_the_next_one()
    {
        using var rig = await WatcherRig.ProgramAsync(_name);
        rig.Processes.Set(Copy(rig.Child!.Pid), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(40));

        rig.Processes.Unreadable();
        await rig.ExitChildAsync();
        await rig.AdvanceAsync(Seconds(6));
        Assert.False(rig.Watcher.IsSettled);

        rig.Processes.Set(ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(2));
        var outcome = await rig.OutcomeAsync();

        Assert.Equal(SessionEndKind.ChildExited, outcome.Kind);
        Assert.Equal(Seconds(40), outcome.PlayDuration);
    }

    [Fact]
    public async Task A_copy_already_running_before_the_program_started_is_followed_after_the_program_quits()
    {
        using var rig = await WatcherRig.ProgramAsync(_name);
        rig.Processes.Set(Copy(rig.Child!.Pid), Copy(901), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(40));

        rig.Processes.Set(Copy(901), ScriptedInspector.Bystander);
        await rig.ExitChildAsync();
        await rig.AdvanceAsync(Seconds(60));
        Assert.False(rig.Watcher.IsSettled);

        rig.Processes.Set(ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(2));
        var outcome = await rig.OutcomeAsync();

        Assert.Equal(SessionEndKind.NamedProcessEnded, outcome.Kind);
        Assert.Equal([901], outcome.FollowedPids);
    }

    [Fact]
    public async Task A_game_that_starts_a_child_under_its_own_name_and_quits_is_followed_until_the_child_is_gone()
    {
        using var rig = await WatcherRig.ProgramAsync(_name);
        rig.Processes.Set(Copy(rig.Child!.Pid), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(10));
        rig.Processes.Set(Copy(rig.Child.Pid), Copy(902), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(40));

        rig.Processes.Set(Copy(902), ScriptedInspector.Bystander);
        await rig.ExitChildAsync();
        await rig.AdvanceAsync(Seconds(60));
        Assert.False(rig.Watcher.IsSettled);

        rig.Processes.Set(ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(2));

        Assert.Equal([902], (await rig.OutcomeAsync()).FollowedPids);
    }

    [Fact]
    public async Task Every_other_copy_that_was_seen_is_named_in_the_outcome_once()
    {
        using var rig = await WatcherRig.ProgramAsync(_name);
        rig.Processes.Set(Copy(rig.Child!.Pid), Copy(902), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(4));
        rig.Processes.Set(Copy(902), Copy(901), ScriptedInspector.Bystander);
        await rig.ExitChildAsync();
        await rig.AdvanceAsync(Seconds(4));

        rig.Processes.Set(ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(2));
        var outcome = await rig.OutcomeAsync();

        Assert.Equal(rig.Child.Pid, outcome.StartedPid);
        Assert.Equal([901, 902], outcome.FollowedPids);
    }

    [Fact]
    public async Task A_program_that_was_never_seen_under_the_name_gets_the_grace_even_after_a_quick_exit()
    {
        using var rig = await WatcherRig.ProgramAsync(_name);
        await rig.AdvanceAsync(Seconds(4));

        await rig.ExitChildAsync();
        await rig.AdvanceAsync(Seconds(29));
        Assert.False(rig.Watcher.IsSettled);
        await rig.AdvanceAsync(Seconds(1));

        Assert.Equal(SessionEndKind.NamedProcessNeverAppeared, (await rig.OutcomeAsync()).Kind);
    }

    [Fact]
    public void A_negative_relaunch_window_is_refused()
    {
        var options = new GameSessionOptions { RelaunchWindow = Seconds(-1) };

        Assert.Throws<ArgumentOutOfRangeException>(() => new GameSessionWatcher(null, _name, new ScriptedInspector(), OsKind.Windows, options, TimeProvider.System));
    }

    [Fact]
    public async Task A_program_that_exits_with_an_error_and_is_not_started_again_settles_when_the_grace_runs_out()
    {
        using var rig = await WatcherRig.ProgramAsync(_name);
        rig.Processes.Set(Copy(rig.Child!.Pid), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(4));

        rig.Processes.Set(ScriptedInspector.Bystander);
        await rig.ExitChildAsync(1);
        await rig.AdvanceAsync(Seconds(29));
        Assert.False(rig.Watcher.IsSettled);
        await rig.AdvanceAsync(Seconds(1));
        var outcome = await rig.OutcomeAsync();

        Assert.Equal(SessionEndKind.ChildExited, outcome.Kind);
        Assert.Equal(Seconds(4), outcome.PlayDuration);
    }

    [Fact]
    public async Task With_no_grace_the_session_settles_at_the_first_look_after_the_stub_exits()
    {
        var clock = new Services.ManualTimeProvider();
        var processes = new ScriptedInspector();
        var child = new ScriptedProcess();
        using var watcher = new GameSessionWatcher(child, _name, processes, OsKind.Windows, new GameSessionOptions { PollInterval = Seconds(2), Grace = TimeSpan.Zero }, clock);
        await WatcherRig.UntilAsync(() => clock.PendingTimers > 0);

        child.Exit();
        var outcome = await watcher.Outcome.WaitAsync(Seconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(SessionEndKind.NamedProcessNeverAppeared, outcome.Kind);
        Assert.Equal(TimeSpan.Zero, outcome.PlayDuration);
    }

    [Theory]
    [InlineData(0, 30)]
    [InlineData(-2, 30)]
    [InlineData(2, -1)]
    public void Timings_that_would_spin_or_never_wait_are_refused(int intervalSeconds, int graceSeconds)
    {
        var options = new GameSessionOptions { PollInterval = Seconds(intervalSeconds), Grace = Seconds(graceSeconds) };

        Assert.Throws<ArgumentOutOfRangeException>(() => new GameSessionWatcher(null, _name, new ScriptedInspector(), OsKind.Windows, options, TimeProvider.System));
    }

    // ---- reading the process list ----

    [Fact]
    public async Task An_unreadable_process_list_is_not_taken_for_an_exit()
    {
        using var rig = await WatcherRig.ProgramAsync(_name);
        rig.Processes.Set(WatcherRig.Game, ScriptedInspector.Bystander);
        await rig.ExitChildAsync();
        await rig.AdvanceAsync(Seconds(4));

        rig.Processes.Unreadable();
        await rig.AdvanceAsync(Seconds(60));
        Assert.False(rig.Watcher.IsSettled);

        rig.Processes.Set(WatcherRig.Game, ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(10));
        Assert.False(rig.Watcher.IsSettled);
        rig.Processes.Set(ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(2));

        Assert.Equal(SessionEndKind.NamedProcessEnded, (await rig.OutcomeAsync()).Kind);
    }

    [Fact]
    public async Task An_inspector_that_throws_is_treated_like_an_unreadable_list()
    {
        using var rig = await WatcherRig.ProgramAsync(_name);
        rig.Processes.Set(WatcherRig.Game, ScriptedInspector.Bystander);
        await rig.ExitChildAsync();
        await rig.AdvanceAsync(Seconds(4));

        rig.Processes.Throws = true;
        await rig.AdvanceAsync(Seconds(60));

        Assert.False(rig.Watcher.IsSettled);
    }

    [Fact]
    public async Task A_list_that_stays_unreadable_for_the_whole_grace_settles_as_unreadable()
    {
        using var rig = await WatcherRig.ProgramAsync(_name);
        rig.Processes.Unreadable();
        await rig.ExitChildAsync();

        await rig.AdvanceAsync(WatcherRig.Grace);

        Assert.Equal(SessionEndKind.ProcessListUnreadable, (await rig.OutcomeAsync()).Kind);
    }

    [Fact]
    public async Task The_list_is_read_once_per_interval()
    {
        using var rig = await WatcherRig.LinkAsync(_name);
        Assert.Equal(1, rig.Processes.Calls);

        await rig.AdvanceAsync(Seconds(10));

        Assert.Equal(6, rig.Processes.Calls);
    }

    [Fact]
    public async Task On_Linux_the_name_is_matched_case_sensitively_and_a_Proton_game_by_its_exe_name()
    {
        using var rig = await WatcherRig.LinkAsync("Game.exe", OsKind.Linux);
        rig.Processes.Set(new RunningProcess(7, "Gam", null, @"Z:\games\Game.exe"), ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(4));
        rig.Processes.Set(ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(2));

        Assert.Equal(SessionEndKind.NamedProcessEnded, (await rig.OutcomeAsync()).Kind);
    }

    [Fact]
    public async Task On_Linux_a_native_name_that_differs_only_in_case_is_a_different_program()
    {
        using var rig = await WatcherRig.LinkAsync("game", OsKind.Linux);
        rig.Processes.Set(new RunningProcess(7, "Game", null), ScriptedInspector.Bystander);

        await rig.AdvanceAsync(WatcherRig.Grace);

        Assert.Equal(SessionEndKind.NamedProcessNeverAppeared, (await rig.OutcomeAsync()).Kind);
    }

    // ---- a link: there is no program, so the process list is all there is ----

    [Fact]
    public async Task With_the_default_timings_a_launcher_that_starts_the_game_after_a_minute_is_still_followed()
    {
        // A launcher or anti-cheat bootstrap can take well over half a minute before the game's own process exists.
        using var rig = await WatcherRig.LinkAsync(_name, options: new GameSessionOptions { PollInterval = WatcherRig.Interval });
        await rig.AdvanceAsync(Seconds(90));
        Assert.False(rig.Watcher.IsSettled);

        rig.Processes.Set(WatcherRig.Game, ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(10));
        Assert.False(rig.Watcher.IsSettled);

        rig.Processes.Set(ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(2));

        Assert.Equal(SessionEndKind.NamedProcessEnded, (await rig.OutcomeAsync()).Kind);
    }

    [Fact]
    public async Task With_the_default_timings_a_name_that_never_appears_settles_at_two_minutes_and_not_before()
    {
        using var rig = await WatcherRig.LinkAsync(_name, options: new GameSessionOptions());

        await rig.AdvanceAsync(TimeSpan.FromMinutes(2) - Seconds(1));
        Assert.False(rig.Watcher.IsSettled);
        await rig.AdvanceAsync(Seconds(1));
        var outcome = await rig.OutcomeAsync();

        Assert.Equal(SessionEndKind.NamedProcessNeverAppeared, outcome.Kind);
        Assert.Equal(TimeSpan.FromMinutes(2), outcome.PlayDuration);
    }

    [Fact]
    public async Task A_link_with_a_named_process_ends_when_the_process_has_been_seen_and_is_gone()
    {
        using var rig = await WatcherRig.LinkAsync(_name);
        await rig.AdvanceAsync(Seconds(6));
        rig.Processes.Set(WatcherRig.Game, ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(10));
        Assert.False(rig.Watcher.IsSettled);

        rig.Processes.Set(ScriptedInspector.Bystander);
        await rig.AdvanceAsync(Seconds(2));
        var outcome = await rig.OutcomeAsync();

        Assert.Equal(SessionEndKind.NamedProcessEnded, outcome.Kind);
        Assert.Null(outcome.ChildExitCode);
        Assert.Equal(Seconds(18), outcome.PlayDuration);
    }

    [Fact]
    public async Task A_link_whose_process_never_appears_settles_when_the_grace_since_it_was_opened_runs_out()
    {
        using var rig = await WatcherRig.LinkAsync(_name);

        await rig.AdvanceAsync(Seconds(29));
        Assert.False(rig.Watcher.IsSettled);
        await rig.AdvanceAsync(Seconds(1));
        var outcome = await rig.OutcomeAsync();

        Assert.Equal(SessionEndKind.NamedProcessNeverAppeared, outcome.Kind);
        Assert.Equal(Seconds(30), outcome.PlayDuration);
    }

    [Fact]
    public async Task A_link_with_no_process_name_is_ended_only_by_the_player()
    {
        using var rig = await WatcherRig.LinkAsync();

        await rig.AdvanceAsync(TimeSpan.FromHours(24));
        Assert.False(rig.Watcher.IsSettled);
        Assert.Equal(0, rig.Processes.Calls);

        Assert.True(rig.Watcher.MarkFinished());
        var outcome = await rig.OutcomeAsync();

        Assert.Equal(SessionEndKind.ManuallyFinished, outcome.Kind);
        Assert.Equal(TimeSpan.FromHours(24), outcome.PlayDuration);
    }

    // ---- "I'm done playing" ----

    [Fact]
    public async Task Marking_a_program_finished_ends_the_session_at_once_and_leaves_the_program_alone()
    {
        using var rig = await WatcherRig.ProgramAsync();
        await rig.AdvanceAsync(TimeSpan.FromMinutes(5));

        Assert.True(await rig.FinishAsync());
        var outcome = await rig.OutcomeAsync();

        Assert.Equal(SessionEndKind.ManuallyFinished, outcome.Kind);
        Assert.Equal(TimeSpan.FromMinutes(5), outcome.PlayDuration);
        Assert.Null(outcome.ChildExitCode);
        Assert.False(rig.Child!.HasExited);
        Assert.False(rig.Child.Disposed);
    }

    [Fact]
    public async Task Marking_finished_while_the_stub_is_being_followed_ends_the_wait_without_the_grace()
    {
        using var rig = await WatcherRig.ProgramAsync(_name);
        await rig.ExitChildAsync(4);
        await rig.AdvanceAsync(Seconds(3));

        Assert.True(await rig.FinishAsync());
        var outcome = await rig.OutcomeAsync();

        Assert.Equal(SessionEndKind.ManuallyFinished, outcome.Kind);
        Assert.Equal(Seconds(3), outcome.PlayDuration);
        Assert.Equal(4, outcome.ChildExitCode);
    }

    [Fact]
    public async Task Marking_finished_stops_the_polling()
    {
        using var rig = await WatcherRig.LinkAsync(_name);
        await rig.AdvanceAsync(Seconds(4));

        await rig.FinishAsync();
        await WatcherRig.UntilAsync(() => rig.Clock.PendingTimers == 0);
        var calls = rig.Processes.Calls;
        rig.Clock.Advance(TimeSpan.FromMinutes(1));
        await Task.Delay(_noMoreLooks, TestContext.Current.CancellationToken);

        Assert.Equal(calls, rig.Processes.Calls);
    }

    [Fact]
    public async Task Marking_finished_after_the_session_ended_changes_nothing()
    {
        using var rig = await WatcherRig.ProgramAsync();
        rig.Child!.Exit(5);
        var first = await rig.OutcomeAsync();

        Assert.False(rig.Watcher.MarkFinished());

        Assert.Equal(first, await rig.OutcomeAsync());
        Assert.Equal(SessionEndKind.ChildExited, first.Kind);
    }

    [Fact]
    public async Task Marking_finished_twice_ends_it_once()
    {
        using var rig = await WatcherRig.LinkAsync();

        Assert.True(rig.Watcher.MarkFinished());
        Assert.False(rig.Watcher.MarkFinished());
    }

    // ---- is the game running? ----

    [Fact]
    public async Task A_tracked_program_counts_as_running_until_it_exits()
    {
        using var rig = await WatcherRig.ProgramAsync();
        Assert.True(rig.Watcher.IsRunning());

        rig.Child!.Exit();
        await rig.OutcomeAsync();

        Assert.False(rig.Watcher.IsRunning());
    }

    [Fact]
    public async Task After_the_stub_exits_the_named_process_decides_whether_the_game_is_running()
    {
        using var rig = await WatcherRig.ProgramAsync(_name);
        rig.Processes.Set(ScriptedInspector.Bystander);
        await rig.ExitChildAsync();
        Assert.False(rig.Watcher.IsRunning());

        rig.Processes.Set(WatcherRig.Game, ScriptedInspector.Bystander);
        Assert.True(rig.Watcher.IsRunning());

        rig.Processes.Set(ScriptedInspector.Bystander);
        Assert.False(rig.Watcher.IsRunning());
    }

    [Fact]
    public async Task A_session_the_player_ended_still_reports_a_game_that_is_still_open()
    {
        using var rig = await WatcherRig.ProgramAsync();
        await rig.FinishAsync();

        Assert.True(rig.Watcher.IsRunning());
    }

    [Fact]
    public async Task A_link_with_no_process_name_has_nothing_to_report_as_running()
    {
        using var rig = await WatcherRig.LinkAsync();

        Assert.False(rig.Watcher.IsRunning());
    }

    [Fact]
    public async Task A_disposed_watcher_reports_nothing_running()
    {
        var rig = await WatcherRig.ProgramAsync();
        rig.Dispose();

        Assert.False(rig.Watcher.IsRunning());
    }

    // ---- letting go ----

    [Fact]
    public async Task Disposing_an_unfinished_session_cancels_its_outcome_and_releases_the_program_without_stopping_it()
    {
        var rig = await WatcherRig.ProgramAsync(_name);

        rig.Dispose();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.Watcher.Outcome);
        await WatcherRig.UntilAsync(() => rig.Child!.Disposed);
        Assert.False(rig.Child!.HasExited);
    }

    [Fact]
    public async Task Disposing_stops_the_polling()
    {
        var rig = await WatcherRig.LinkAsync(_name);
        await rig.AdvanceAsync(Seconds(4));

        rig.Dispose();
        await WatcherRig.UntilAsync(() => rig.Clock.PendingTimers == 0);
        var calls = rig.Processes.Calls;
        rig.Clock.Advance(TimeSpan.FromMinutes(1));
        await Task.Delay(_noMoreLooks, TestContext.Current.CancellationToken);

        Assert.Equal(calls, rig.Processes.Calls);
    }

    [Fact]
    public async Task A_program_that_exits_just_after_the_wait_is_armed_is_noticed_without_the_clock_moving()
    {
        var clock = new HookedTimeProvider(new ManualTimeProvider());
        var child = new ScriptedProcess();
        var processes = new ScriptedInspector();
        clock.AfterTimerCreated = () => child.Exit();
        var options = new GameSessionOptions { PollInterval = WatcherRig.Interval, Grace = WatcherRig.Grace };

        using var watcher = new GameSessionWatcher(child, _name, processes, OsKind.Windows, options, clock);
        var looked = WatcherRig.UntilAsync(() => processes.Calls > 1);
        var finished = await Task.WhenAny(looked, Task.Delay(Seconds(5), TestContext.Current.CancellationToken));

        Assert.Same(looked, finished);
    }

    [Fact]
    public async Task Disposing_a_session_that_already_ended_keeps_its_outcome()
    {
        var rig = await WatcherRig.ProgramAsync();
        rig.Child!.Exit(6);
        var outcome = await rig.OutcomeAsync();

        rig.Dispose();

        Assert.Equal(outcome, await rig.Watcher.Outcome);
    }
}
