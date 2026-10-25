using Savepoint.Core.Catalog;
using Savepoint.Core.Launch;
using Savepoint.Core.Platform;
using Savepoint.Core.Tests.Services.Exe;

namespace Savepoint.Core.Tests.Launch;

// Start the stand-in game through the real launcher and the real process list, and watch what happens.
public class RealSessionTests
{
    private static readonly TimeSpan _patience = TimeSpan.FromSeconds(30);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // The real process list, counting how often the watcher reads it so a test can wait for the watcher's looks instead of for a time.
    private sealed class CountingInspector(IProcessInspector inner) : IProcessInspector
    {
        private readonly object _gate = new();
        private readonly List<(int Reads, TaskCompletionSource Reached)> _waiters = [];
        private int _reads;

        public IReadOnlyList<RunningProcess> List()
        {
            var result = inner.List();
            lock (_gate)
            {
                _reads++;
                foreach (var waiter in _waiters.Where(waiter => _reads >= waiter.Reads))
                {
                    waiter.Reached.TrySetResult();
                }
            }

            return result;
        }

        // Completes once the list has been read this many more times than it has been so far.
        public Task ReadAgainAsync(int times)
        {
            lock (_gate)
            {
                var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiters.Add((_reads + times, reached));
                return reached.Task.WaitAsync(_patience, Ct);
            }
        }
    }

    private static GameStarter Starter(TimeSpan? grace = null, IGameLauncher? launcher = null, IProcessInspector? processes = null) => new(
        launcher ?? GameLaunchers.ForCurrentOs(),
        processes ?? FakeGameInstall.Inspector(),
        PlatformFactory.CurrentOs,
        new GameSessionOptions { PollInterval = TimeSpan.FromMilliseconds(100), Grace = grace ?? TimeSpan.FromSeconds(3) },
        TimeProvider.System);

    private static LaunchConfig Program(FakeGameInstall game, string? processName, params string[] args) =>
        new(LaunchMethod.Exe, Path.GetFileName(game.ExecutablePath), null, args, processName);

    private static async Task<SessionOutcome> OutcomeAsync(GameSessionWatcher watcher) => await watcher.Outcome.WaitAsync(_patience, Ct);

    [Fact]
    public async Task A_game_that_exits_by_itself_ends_the_session_with_its_exit_code_and_a_real_duration()
    {
        using var game = new FakeGameInstall();

        using var watcher = await Starter().StartAsync(Program(game, null, "--exit-after", "400", "--exit-code", "5"), game.Folder, Ct);
        var outcome = await OutcomeAsync(watcher);

        Assert.Equal(SessionEndKind.ChildExited, outcome.Kind);
        Assert.Equal(5, outcome.ChildExitCode);
        Assert.True(outcome.PlayDuration >= TimeSpan.FromMilliseconds(200), $"the session lasted only {outcome.PlayDuration}");
    }

    [Fact]
    public async Task A_launcher_stub_that_hands_over_to_the_real_game_is_followed_until_that_game_leaves()
    {
        using var stub = new FakeGameInstall();
        using var game = new FakeGameInstall();
        var gameReady = game.Combine("ready.txt");
        var config = Program(stub, game.Name, "--spawn", game.ExecutablePath, "--ready-file", gameReady, "--exit-after", "2500");

        using var watcher = await Starter().StartAsync(config, stub.Folder, Ct);
        var outcome = await OutcomeAsync(watcher);
        var gamePid = await FakeGameProcess.WaitForReadyFileAsync(gameReady);

        Assert.Equal(SessionEndKind.NamedProcessEnded, outcome.Kind);
        Assert.Equal(0, outcome.ChildExitCode);
        Assert.True(outcome.PlayDuration >= TimeSpan.FromSeconds(1), $"the session ended after only {outcome.PlayDuration}, before the real game left");
        Assert.False(FakeGameProcess.IsRunning(gamePid), "the session ended while the real game was still running");
    }

    [Fact]
    public async Task A_starter_named_in_the_definition_that_hands_over_to_a_process_it_started_is_followed_until_that_process_leaves()
    {
        using var stub = new FakeGameInstall();
        var gameReady = stub.Combine("ready.txt");
        // The real game lives in the install folder under a name the definition does not know.
        var realGame = Path.Combine(stub.Folder, "real" + Path.GetExtension(stub.ExecutablePath));
        File.Copy(stub.ExecutablePath, realGame);
        var config = Program(stub, stub.Name, "--spawn", realGame, "--ready-file", gameReady, "--exit-after", "4000");

        using var watcher = await Starter(grace: TimeSpan.FromSeconds(1)).StartAsync(config, stub.Folder, Ct);
        var outcome = await OutcomeAsync(watcher);
        var gamePid = await FakeGameProcess.WaitForReadyFileAsync(gameReady);

        Assert.Equal(SessionEndKind.NamedProcessEnded, outcome.Kind);
        Assert.Contains(gamePid, outcome.FollowedPids);
        Assert.True(outcome.PlayDuration >= TimeSpan.FromSeconds(3), $"the session ended after only {outcome.PlayDuration}, before the started game left");
        Assert.False(FakeGameProcess.IsRunning(gamePid), "the session ended while the game the starter started was still running");
    }

    [Fact]
    public async Task A_game_that_exits_at_once_and_is_started_again_by_another_program_is_followed_to_the_end_of_the_new_copy()
    {
        using var game = new FakeGameInstall();
        var secondReady = game.Combine("second.txt");
        var firstReady = game.Combine("first.txt");
        var config = Program(game, game.Name, "--ready-file", firstReady, "--exit-after", "600");

        using var watcher = await Starter(grace: TimeSpan.FromSeconds(20)).StartAsync(config, game.Folder, Ct);
        var firstPid = await FakeGameProcess.WaitForReadyFileAsync(firstReady);

        // The store starts the new copy only after the first one is gone, which is what makes this a relaunch.
        Assert.True(await FakeGameProcess.WaitUntilAsync(() => !FakeGameProcess.IsRunning(firstPid)), "the first copy never exited");
        using var store = FakeGameProcess.StartIn(game.Folder, "--spawn", game.ExecutablePath, "--ready-file", secondReady, "--exit-after", "2000");
        var secondPid = await FakeGameProcess.WaitForReadyFileAsync(secondReady);
        var outcome = await OutcomeAsync(watcher);

        Assert.Equal(SessionEndKind.NamedProcessEnded, outcome.Kind);
        Assert.True(outcome.PlayDuration >= TimeSpan.FromSeconds(2), $"the session ended after only {outcome.PlayDuration}, before the new copy left");
        Assert.False(FakeGameProcess.IsRunning(secondPid), "the session ended while the new copy was still running");
    }

    [Fact]
    public async Task A_name_that_never_shows_up_settles_the_session_after_the_grace_counted_from_the_stubs_exit()
    {
        using var stub = new FakeGameInstall();
        var config = Program(stub, "fgnever" + Guid.NewGuid().ToString("N")[..6], "--exit-after", "200");

        using var watcher = await Starter(grace: TimeSpan.FromSeconds(1)).StartAsync(config, stub.Folder, Ct);
        var outcome = await OutcomeAsync(watcher);

        Assert.Equal(SessionEndKind.NamedProcessNeverAppeared, outcome.Kind);
        Assert.Equal(0, outcome.ChildExitCode);
        Assert.True(outcome.PlayDuration >= TimeSpan.FromSeconds(1), $"the session settled after {outcome.PlayDuration}");
    }

    [Fact]
    public async Task Marking_a_running_game_finished_ends_the_session_and_leaves_the_game_running()
    {
        using var game = new FakeGameInstall();
        var ready = game.Combine("ready.txt");

        using var watcher = await Starter().StartAsync(Program(game, null, "--ready-file", ready), game.Folder, Ct);
        var pid = await FakeGameProcess.WaitForReadyFileAsync(ready);

        Assert.True(watcher.MarkFinished());
        var outcome = await OutcomeAsync(watcher);

        Assert.Equal(SessionEndKind.ManuallyFinished, outcome.Kind);
        Assert.Null(outcome.ChildExitCode);
        Assert.True(FakeGameProcess.IsRunning(pid), "marking the session finished must not stop the game");
        Assert.True(watcher.IsRunning(), "the player may still have the game open");
    }

    [Fact]
    public async Task A_game_is_recognized_as_running_by_its_process_name_before_a_launch_and_not_after_it_leaves()
    {
        using var game = new FakeGameInstall();
        var ready = game.Combine("ready.txt");
        var starter = Starter();
        var config = Program(game, game.Name, "--ready-file", ready);
        Assert.False(starter.IsRunning(config));

        using var watcher = await starter.StartAsync(config, game.Folder, Ct);
        var pid = await FakeGameProcess.WaitForReadyFileAsync(ready);

        Assert.True(starter.IsRunning(config));
        Assert.True(watcher.IsRunning());

        FakeGameProcess.Kill(pid);
        await WatcherRig.UntilAsync(() => !starter.IsRunning(config));
        var outcome = await OutcomeAsync(watcher);

        // Whether the poll caught the game by name before the kill is a matter of timing; either way the program was the game.
        Assert.True(outcome.Kind is SessionEndKind.ChildExited or SessionEndKind.NamedProcessNeverAppeared, $"{outcome.Kind}");
        Assert.False(watcher.IsRunning());
    }

    [Fact]
    public async Task A_link_session_follows_a_real_process_by_name_and_has_no_exit_code()
    {
        using var game = new FakeGameInstall();
        var launcher = new RecordingLauncher();
        var ready = game.Combine("ready.txt");
        using var running = GameLaunchers.ForCurrentOs().StartExecutable(game.ExecutablePath, ["--ready-file", ready], game.Folder);
        var pid = await FakeGameProcess.WaitForReadyFileAsync(ready);

        var processes = new CountingInspector(FakeGameInstall.Inspector());
        using var watcher = await Starter(launcher: launcher, processes: processes).StartAsync(new LaunchConfig(LaunchMethod.Uri, null, "steam://run/1", null, game.Name), game.Folder, Ct);

        // The game stays until the test stops it, so the order of "the game left" and "the session ended" does not depend on a timer or on how
        // soon this process collects a finished child: a session that settled before the stop settled while the game ran. The watcher has looked
        // at the running game twice by then, so a session that ends at the first sight of it has ended already.
        await processes.ReadAgainAsync(2);
        var stoppedAt = DateTimeOffset.UtcNow;
        FakeGameProcess.Kill(pid);
        var outcome = await OutcomeAsync(watcher);

        Assert.Equal(["steam://run/1"], launcher.Links);
        Assert.Equal(SessionEndKind.NamedProcessEnded, outcome.Kind);
        Assert.Null(outcome.ChildExitCode);
        Assert.True(outcome.EndedAt >= stoppedAt, $"the session ended at {outcome.EndedAt:O}, before the game was stopped at {stoppedAt:O}");
    }

    [Fact]
    public async Task A_link_whose_game_never_appears_settles_after_the_grace_as_best_effort()
    {
        var launcher = new RecordingLauncher();
        using var watcher = await Starter(grace: TimeSpan.FromSeconds(1), launcher: launcher)
            .StartAsync(new LaunchConfig(LaunchMethod.Uri, null, "steam://run/1", null, "fgnever" + Guid.NewGuid().ToString("N")[..6]), Path.GetTempPath(), Ct);

        var outcome = await OutcomeAsync(watcher);

        Assert.Equal(SessionEndKind.NamedProcessNeverAppeared, outcome.Kind);
        Assert.True(outcome.PlayDuration >= TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task A_link_with_no_process_name_is_only_ever_ended_by_the_player()
    {
        var launcher = new RecordingLauncher();
        using var watcher = await Starter(launcher: launcher).StartAsync(new LaunchConfig(LaunchMethod.Uri, null, "steam://run/1"), Path.GetTempPath(), Ct);

        await Task.Delay(500, Ct);
        Assert.False(watcher.IsSettled);

        Assert.True(watcher.MarkFinished());
        Assert.Equal(SessionEndKind.ManuallyFinished, (await OutcomeAsync(watcher)).Kind);
    }
}
