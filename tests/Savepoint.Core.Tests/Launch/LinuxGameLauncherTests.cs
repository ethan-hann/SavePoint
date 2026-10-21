using System.ComponentModel;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Launch;
using Savepoint.Core.Tests.Services;

namespace Savepoint.Core.Tests.Launch;

public class LinuxGameLauncherTests
{
    private const string _install = "/games/alpha";
    private const string _binary = "/games/alpha/alpha";
    private static readonly TimeSpan _openerWait = TimeSpan.FromSeconds(10);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (LinuxGameLauncher Launcher, RecordingStarter Starter, ManualTimeProvider Clock) Build(Action<FakeLaunchFiles>? files = null)
    {
        var fake = new FakeLaunchFiles().WithFile(_binary, executable: true);
        files?.Invoke(fake);
        var starter = new RecordingStarter();
        var clock = new ManualTimeProvider();
        return (new LinuxGameLauncher(starter, fake, _openerWait, clock), starter, clock);
    }

    [Fact]
    public void A_native_binary_with_an_execute_bit_starts_directly_in_the_install_folder()
    {
        var (launcher, starter, _) = Build();

        launcher.StartExecutable(_binary, ["-windowed", "two words"], _install);

        Assert.Equal(new ProcessStartRequest(_binary, ["-windowed", "two words"], _install, UseShellExecute: false), starter.Only, new RequestComparer());
    }

    [Fact]
    public void The_caller_gets_the_process_the_starter_returned()
    {
        var (launcher, starter, _) = Build();
        var process = new ScriptedProcess(31);
        starter.Answer = _ => process;

        Assert.Same(process, launcher.StartExecutable(_binary, [], _install));
    }

    [Fact]
    public void A_binary_without_an_execute_bit_is_refused_and_its_permissions_are_left_alone()
    {
        var (launcher, starter, _) = Build(files => files.WithFile("/games/alpha/plain"));

        var error = Assert.Throws<LaunchSpawnException>(() => launcher.StartExecutable("/games/alpha/plain", [], _install));

        Assert.Equal(LaunchMethod.Exe, error.Args["method"]);
        Assert.Contains("execute permission", error.Message);
        Assert.Empty(starter.Requests);
    }

    [Theory]
    [InlineData("/games/alpha/run.sh")]
    [InlineData("/games/alpha/RUN.SH")]
    public void A_shell_script_runs_through_bin_sh_even_without_an_execute_bit(string script)
    {
        var (launcher, starter, _) = Build(files => files.WithFile(script));

        launcher.StartExecutable(script, ["--fast", "a b"], _install);

        Assert.Equal(new ProcessStartRequest("/bin/sh", [script, "--fast", "a b"], _install, UseShellExecute: false), starter.Only, new RequestComparer());
    }

    [Theory]
    [InlineData("/games/alpha/Game.exe")]
    [InlineData("/games/alpha/GAME.EXE")]
    public void A_Windows_program_is_refused_with_its_own_error_and_not_started(string program)
    {
        var (launcher, starter, _) = Build(files => files.WithFile(program, executable: true));

        var error = Assert.Throws<LaunchWindowsProgramException>(() => launcher.StartExecutable(program, [], _install));

        Assert.Equal(program, error.Args["path"]);
        Assert.Empty(starter.Requests);
    }

    [Theory]
    [InlineData("/games/alpha/gone")]
    [InlineData("/games/alpha/gone.exe")]
    [InlineData("/games/alpha/gone.sh")]
    public void A_missing_file_is_reported_as_a_missing_install_path_before_any_other_rule(string path)
    {
        var (launcher, starter, _) = Build();

        var error = Assert.Throws<PathMissingException>(() => launcher.StartExecutable(path, [], _install));

        Assert.Equal(path, error.Args["path"]);
        Assert.Equal(RepoRole.Install, error.Args["role"]);
        Assert.Empty(starter.Requests);
    }

    [Fact]
    public void A_start_the_OS_refuses_becomes_an_exe_spawn_failure_that_keeps_the_cause()
    {
        var (launcher, starter, _) = Build();
        var cause = new Win32Exception(13, "Permission denied");
        starter.Answer = _ => throw cause;

        var error = Assert.Throws<LaunchSpawnException>(() => launcher.StartExecutable(_binary, [], _install));

        Assert.Equal(LaunchMethod.Exe, error.Args["method"]);
        Assert.Same(cause, error.InnerException);
        Assert.Contains(_binary, error.Message);
    }

    [Fact]
    public async Task A_link_goes_to_xdg_open_as_its_only_argument_and_the_opener_is_released()
    {
        var (launcher, starter, _) = Build();
        var opener = new ScriptedProcess();
        starter.Answer = _ => opener;
        opener.Exit(0);

        await launcher.OpenUriAsync("steam://run/489830", Ct);

        Assert.Equal(new ProcessStartRequest("xdg-open", ["steam://run/489830"], null, UseShellExecute: false), starter.Only, new RequestComparer());
        Assert.True(opener.Disposed);
    }

    [Fact]
    public async Task An_opener_that_fails_is_a_uri_spawn_failure_naming_its_exit_code()
    {
        var (launcher, starter, _) = Build();
        var opener = new ScriptedProcess();
        opener.Exit(4);
        starter.Answer = _ => opener;

        var error = await Assert.ThrowsAsync<LaunchSpawnException>(() => launcher.OpenUriAsync("steam://run/489830", Ct));

        Assert.Equal(LaunchMethod.Uri, error.Args["method"]);
        Assert.Contains("exit code 4", error.Message);
        Assert.True(opener.Disposed);
    }

    [Fact]
    public async Task An_opener_still_running_after_the_wait_counts_as_handed_over_and_is_not_stopped()
    {
        var (launcher, starter, clock) = Build();
        var opener = new ScriptedProcess();
        starter.Answer = _ => opener;

        var opening = launcher.OpenUriAsync("steam://run/489830", Ct);
        Assert.False(opening.IsCompleted);
        clock.Advance(_openerWait);
        await opening;

        Assert.True(opener.Disposed);
        Assert.False(opener.HasExited);
    }

    [Fact]
    public async Task A_missing_opener_is_a_uri_spawn_failure_that_keeps_the_cause()
    {
        var (launcher, starter, _) = Build();
        var cause = new Win32Exception(2, "No such file or directory");
        starter.Answer = _ => throw cause;

        var error = await Assert.ThrowsAsync<LaunchSpawnException>(() => launcher.OpenUriAsync("steam://run/489830", Ct));

        Assert.Equal(LaunchMethod.Uri, error.Args["method"]);
        Assert.Same(cause, error.InnerException);
    }

    [Fact]
    public async Task Canceling_while_the_opener_runs_stops_the_wait_but_not_the_opener()
    {
        var (launcher, starter, _) = Build();
        var opener = new ScriptedProcess();
        starter.Answer = _ => opener;
        using var cancel = new CancellationTokenSource();

        var opening = launcher.OpenUriAsync("steam://run/489830", cancel.Token);
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => opening);
        Assert.False(opener.HasExited);
        Assert.True(opener.Disposed);
    }

    [Fact]
    public async Task A_link_is_not_opened_when_the_token_is_already_canceled()
    {
        var (launcher, starter, _) = Build();
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => launcher.OpenUriAsync("steam://run/489830", cancel.Token));

        Assert.Empty(starter.Requests);
    }
}
