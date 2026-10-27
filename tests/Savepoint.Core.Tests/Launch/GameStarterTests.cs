using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Launch;
using Savepoint.Core.Platform;
using Savepoint.Core.Tests.Services;

namespace Savepoint.Core.Tests.Launch;

// Remembers what it was asked to start and answers with a scripted process, or fails.
internal sealed class RecordingLauncher : IGameLauncher
{
    public List<(string Path, IReadOnlyList<string> Args, string WorkingDirectory)> Programs { get; } = [];

    public List<string> Links { get; } = [];

    public CancellationToken LinkToken { get; private set; }

    public ScriptedProcess Process { get; } = new();

    public Exception? Failure { get; set; }

    // Runs while the launcher is starting the program, to stand for the time a start takes on a busy machine.
    public Action? WhileStarting { get; set; }

    public IGameProcess StartExecutable(string path, IReadOnlyList<string> args, string workingDirectory)
    {
        if (Failure is not null)
        {
            throw Failure;
        }

        WhileStarting?.Invoke();
        Programs.Add((path, args, workingDirectory));
        return Process;
    }

    public Task OpenUriAsync(string uri, CancellationToken cancellationToken = default)
    {
        if (Failure is not null)
        {
            throw Failure;
        }

        Links.Add(uri);
        LinkToken = cancellationToken;
        return Task.CompletedTask;
    }
}

public class GameStarterTests
{
    private const string _install = @"C:\Games\Alpha";

    // A watcher that settled early would do so within a few of its 20 ms polls and its 100 ms grace, and only waiting shows that it does not.
    private static readonly TimeSpan _stillWatching = TimeSpan.FromMilliseconds(600);

    private static GameStarter Build(RecordingLauncher launcher, ScriptedInspector? processes = null, OsKind os = OsKind.Windows) =>
        new(launcher, processes ?? new ScriptedInspector(), os, new GameSessionOptions { PollInterval = TimeSpan.FromMilliseconds(20), Grace = TimeSpan.FromMilliseconds(100) }, TimeProvider.System);

    private static LaunchConfig Program(string exePath, params string[] args) => new(LaunchMethod.Exe, exePath, null, args);

    private static LaunchConfig Link(string uri, string? processName = null) => new(LaunchMethod.Uri, null, uri, null, processName);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(@"Alpha.exe", @"C:\Games\Alpha\Alpha.exe")]
    [InlineData(@"bin/win64/Alpha.exe", @"C:\Games\Alpha\bin\win64\Alpha.exe")]
    [InlineData(@"bin\Alpha.exe", @"C:\Games\Alpha\bin\Alpha.exe")]
    [InlineData(@"./Alpha.exe", @"C:\Games\Alpha\Alpha.exe")]
    public async Task A_program_is_resolved_against_the_install_folder_and_started_there_with_its_arguments(string exePath, string expected)
    {
        var launcher = new RecordingLauncher();

        using var watcher = await Build(launcher).StartAsync(Program(exePath, "-windowed", "two words"), _install, Ct);

        var started = Assert.Single(launcher.Programs);
        Assert.Equal(expected, started.Path);
        Assert.Equal(["-windowed", "two words"], started.Args);
        Assert.Equal(_install, started.WorkingDirectory);
    }

    [Fact]
    public async Task The_session_follows_the_program_the_launcher_started()
    {
        var launcher = new RecordingLauncher();
        using var watcher = await Build(launcher).StartAsync(Program("Alpha.exe"), _install, Ct);
        Assert.False(watcher.IsSettled);
        Assert.True(watcher.IsRunning());

        launcher.Process.Exit(4);
        var outcome = await watcher.Outcome.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.Equal(SessionEndKind.ChildExited, outcome.Kind);
        Assert.Equal(4, outcome.ChildExitCode);
    }

    [Theory]
    [InlineData(@"..\Beta\Beta.exe")]
    [InlineData(@"bin/../../Beta/Beta.exe")]
    [InlineData(@"C:\Other\Beta.exe")]
    [InlineData(@"\Other\Beta.exe")]
    public async Task A_program_outside_the_install_folder_is_refused_and_nothing_is_started(string exePath)
    {
        var launcher = new RecordingLauncher();

        var error = await Assert.ThrowsAsync<LaunchSpawnException>(() => Build(launcher).StartAsync(Program(exePath), _install, Ct));

        Assert.Equal(LaunchMethod.Exe, error.Args["method"]);
        Assert.Empty(launcher.Programs);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_program_launch_with_no_program_named_is_refused(string? exePath)
    {
        var launcher = new RecordingLauncher();

        var error = await Assert.ThrowsAsync<LaunchSpawnException>(() => Build(launcher).StartAsync(new LaunchConfig(LaunchMethod.Exe, exePath), _install, Ct));

        Assert.Equal(LaunchMethod.Exe, error.Args["method"]);
        Assert.Empty(launcher.Programs);
    }

    [Fact]
    public async Task Whatever_the_launcher_refuses_reaches_the_caller_unchanged()
    {
        var launcher = new RecordingLauncher { Failure = new PathMissingException(@"C:\Games\Alpha\Alpha.exe", RepoRole.Install) };

        var error = await Assert.ThrowsAsync<PathMissingException>(() => Build(launcher).StartAsync(Program("Alpha.exe"), _install, Ct));

        Assert.Equal(RepoRole.Install, error.Args["role"]);
    }

    [Fact]
    public async Task A_link_is_handed_to_the_launcher_and_the_session_is_poll_only()
    {
        var launcher = new RecordingLauncher();

        using var watcher = await Build(launcher).StartAsync(Link("steam://run/489830"), _install, Ct);

        Assert.Equal(["steam://run/489830"], launcher.Links);
        Assert.Empty(launcher.Programs);
        Assert.False(watcher.IsSettled);
        Assert.True(watcher.MarkFinished());
        Assert.Null((await watcher.Outcome.WaitAsync(TimeSpan.FromSeconds(10), Ct)).ChildExitCode);
    }

    [Fact]
    public async Task A_link_session_follows_the_named_process()
    {
        var launcher = new RecordingLauncher();
        var processes = new ScriptedInspector();
        processes.Set(WatcherRig.Game, ScriptedInspector.Bystander);
        using var watcher = await Build(launcher, processes).StartAsync(Link("steam://run/489830", " Game.exe "), _install, Ct);

        await Task.Delay(_stillWatching, Ct);
        Assert.False(watcher.IsSettled);
        processes.Set(ScriptedInspector.Bystander);
        var outcome = await watcher.Outcome.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.Equal(SessionEndKind.NamedProcessEnded, outcome.Kind);
    }

    private static RunningProcess SteamReaper(int appId) =>
        new(800, "reaper", null, CommandLine: $"/x/reaper SteamLaunch AppId={appId} -- /x/entry-point --verb=waitforexitandrun");

    [Fact]
    public async Task On_Linux_a_Steam_link_waits_while_Steams_launch_helper_for_that_app_is_running()
    {
        var processes = new ScriptedInspector();
        processes.Set(SteamReaper(489830), ScriptedInspector.Bystander);
        using var watcher = await Build(new RecordingLauncher(), processes, OsKind.Linux).StartAsync(Link("steam://run/489830", "Game.exe"), _install, Ct);

        await Task.Delay(_stillWatching, Ct);
        Assert.False(watcher.IsSettled);

        processes.Set(ScriptedInspector.Bystander);
        var outcome = await watcher.Outcome.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        Assert.Equal(SessionEndKind.NamedProcessNeverAppeared, outcome.Kind);
    }

    [Fact]
    public async Task On_Linux_a_Steam_launch_helper_of_another_app_does_not_hold_a_Steam_link()
    {
        var processes = new ScriptedInspector();
        processes.Set(SteamReaper(4898300), ScriptedInspector.Bystander);
        using var watcher = await Build(new RecordingLauncher(), processes, OsKind.Linux).StartAsync(Link("steam://run/489830", "Game.exe"), _install, Ct);

        var outcome = await watcher.Outcome.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.Equal(SessionEndKind.NamedProcessNeverAppeared, outcome.Kind);
    }

    [Theory]
    [InlineData(OsKind.Windows, "steam://run/489830")]
    [InlineData(OsKind.MacOS, "steam://run/489830")]
    [InlineData(OsKind.Linux, "heroic://launch/legendary/Sugar")]
    public async Task Only_a_Steam_link_on_Linux_waits_for_a_launch_helper(OsKind os, string uri)
    {
        var processes = new ScriptedInspector();
        processes.Set(SteamReaper(489830), ScriptedInspector.Bystander);
        using var watcher = await Build(new RecordingLauncher(), processes, os).StartAsync(Link(uri, "Game.exe"), _install, Ct);

        var outcome = await watcher.Outcome.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.Equal(SessionEndKind.NamedProcessNeverAppeared, outcome.Kind);
    }

    [Fact]
    public async Task The_cancellation_token_reaches_the_link_opener()
    {
        var launcher = new RecordingLauncher();
        using var cancel = new CancellationTokenSource();

        using var watcher = await Build(launcher).StartAsync(Link("steam://run/489830"), _install, cancel.Token);

        Assert.Equal(cancel.Token, launcher.LinkToken);
    }

    [Fact]
    public async Task A_link_launch_with_no_link_is_refused()
    {
        var launcher = new RecordingLauncher();

        var error = await Assert.ThrowsAsync<LaunchSpawnException>(() => Build(launcher).StartAsync(new LaunchConfig(LaunchMethod.Uri), _install, Ct));

        Assert.Equal(LaunchMethod.Uri, error.Args["method"]);
        Assert.Empty(launcher.Links);
    }

    [Fact]
    public async Task A_link_the_launcher_cannot_open_starts_no_session()
    {
        var failure = new LaunchSpawnException(LaunchMethod.Uri, "no handler", null);
        var launcher = new RecordingLauncher { Failure = failure };

        var error = await Assert.ThrowsAsync<LaunchSpawnException>(() => Build(launcher).StartAsync(Link("steam://run/489830"), _install, Ct));

        Assert.Same(failure, error);
    }

    [Fact]
    public void A_game_is_running_when_its_named_process_is_listed()
    {
        var processes = new ScriptedInspector();
        var starter = Build(new RecordingLauncher(), processes);
        var config = new LaunchConfig(LaunchMethod.Exe, "Alpha.exe", null, null, "Game.exe");
        Assert.False(starter.IsRunning(config));

        processes.Set(WatcherRig.Game, ScriptedInspector.Bystander);

        Assert.True(starter.IsRunning(config));
    }

    [Fact]
    public void A_game_with_no_process_name_cannot_be_recognized_as_running()
    {
        var processes = new ScriptedInspector();
        processes.Set(WatcherRig.Game, ScriptedInspector.Bystander);

        Assert.False(Build(new RecordingLauncher(), processes).IsRunning(Program("Alpha.exe")));
        Assert.Equal(0, processes.Calls);
    }

    [Fact]
    public async Task A_session_begins_when_the_program_is_asked_to_start_not_when_the_launcher_returns()
    {
        var clock = new ManualTimeProvider();
        var asked = clock.GetUtcNow();
        var launcher = new RecordingLauncher { WhileStarting = () => clock.Advance(TimeSpan.FromMilliseconds(300)) };
        var starter = new GameStarter(launcher, new ScriptedInspector(), OsKind.Windows, new GameSessionOptions(), clock);

        using var watcher = await starter.StartAsync(Program("Alpha.exe"), _install, Ct);
        launcher.Process.Exit();
        var outcome = await watcher.Outcome.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.Equal(asked, watcher.StartedAt);
        Assert.Equal(asked, outcome.StartedAt);
        Assert.Equal(TimeSpan.FromMilliseconds(300), outcome.PlayDuration);
    }

    [Fact]
    public async Task A_game_that_a_vanished_starter_handed_over_to_while_the_launcher_was_still_returning_is_followed()
    {
        const string folder = "/games/foo";
        var clock = new ManualTimeProvider();
        var asked = clock.GetUtcNow();
        var launcher = new RecordingLauncher { WhileStarting = () => clock.Advance(TimeSpan.FromMilliseconds(300)) };
        var processes = new ScriptedInspector();
        var game = new RunningProcess(600, "real", folder + "/bin/real", ParentPid: 1, StartedAt: asked + TimeSpan.FromMilliseconds(100));
        processes.Set(game, ScriptedInspector.Bystander);
        var options = new GameSessionOptions { PollInterval = WatcherRig.Interval, Grace = WatcherRig.Grace };
        var starter = new GameStarter(launcher, processes, OsKind.Linux, options, clock);

        using var watcher = await starter.StartAsync(new LaunchConfig(LaunchMethod.Exe, "Game.exe", null, null, "Game.exe"), folder, Ct);
        await WatcherRig.UntilAsync(() => clock.PendingTimers > 0);
        var before = processes.Calls;
        launcher.Process.Exit();
        await WatcherRig.UntilAsync(() => watcher.IsSettled || processes.Calls > before);
        for (var seconds = 0; seconds < 40 && !watcher.IsSettled; seconds++)
        {
            await WatcherRig.UntilAsync(() => watcher.IsSettled || clock.PendingTimers > 0);
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.False(watcher.IsSettled, "the game that began before the launcher returned was not taken for the starter's");
        Assert.True(watcher.IsRunning());
    }

    [Fact]
    public void Timings_that_would_spin_are_refused_up_front()
    {
        var options = new GameSessionOptions { PollInterval = TimeSpan.Zero };

        Assert.Throws<ArgumentOutOfRangeException>(() => new GameStarter(new RecordingLauncher(), new ScriptedInspector(), OsKind.Windows, options, TimeProvider.System));
    }
}
