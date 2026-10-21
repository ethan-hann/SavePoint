using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Launch;
using Savepoint.Core.Tests.Services;

namespace Savepoint.Core.Tests.Launch;

// Runs on every OS against the process seam; behavior on a real Mac is not verified by these.
public class MacOSGameLauncherTests
{
    private const string _install = "/Applications/Games/Alpha";
    private const string _bundle = "/Applications/Games/Alpha/Alpha.app";
    private const string _binary = "/Applications/Games/Alpha/alpha";
    private static readonly TimeSpan _openerWait = TimeSpan.FromSeconds(10);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (MacOSGameLauncher Launcher, RecordingStarter Starter, ManualTimeProvider Clock) Build(Action<FakeLaunchFiles>? files = null)
    {
        var fake = new FakeLaunchFiles().WithFolder(_bundle).WithFile(_binary, executable: true);
        files?.Invoke(fake);
        var starter = new RecordingStarter();
        var clock = new ManualTimeProvider();
        return (new MacOSGameLauncher(starter, fake, _openerWait, clock), starter, clock);
    }

    [Fact]
    public void An_app_bundle_opens_a_new_instance_and_waits_until_the_app_quits_with_the_arguments_after_args()
    {
        var (launcher, starter, _) = Build();

        launcher.StartExecutable(_bundle, ["-windowed", "two words"], _install);

        Assert.Equal(
            new ProcessStartRequest("/usr/bin/open", ["-W", "-n", _bundle, "--args", "-windowed", "two words"], _install, UseShellExecute: false),
            starter.Only,
            new RequestComparer());
    }

    [Fact]
    public void An_app_bundle_with_no_arguments_leaves_out_args()
    {
        var (launcher, starter, _) = Build();

        launcher.StartExecutable(_bundle, [], _install);

        Assert.Equal(["-W", "-n", _bundle], starter.Only.Arguments);
    }

    [Theory]
    [InlineData("/Applications/Games/Alpha/Alpha.app/")]
    [InlineData("/Applications/Games/Alpha/Alpha.APP")]
    public void A_bundle_is_recognized_with_a_trailing_slash_or_any_case_of_app(string spelled)
    {
        var (launcher, starter, _) = Build(files => files.WithFolder(spelled.TrimEnd('/')));

        launcher.StartExecutable(spelled, [], _install);

        Assert.Equal(spelled.TrimEnd('/'), starter.Only.Arguments[2]);
    }

    [Fact]
    public void The_caller_gets_the_open_process_that_lives_until_the_app_quits()
    {
        var (launcher, starter, _) = Build();
        var open = new ScriptedProcess(55);
        starter.Answer = _ => open;

        Assert.Same(open, launcher.StartExecutable(_bundle, [], _install));
    }

    [Fact]
    public void A_missing_bundle_is_reported_as_a_missing_install_path_and_nothing_is_started()
    {
        var (launcher, starter, _) = Build();

        var error = Assert.Throws<PathMissingException>(() => launcher.StartExecutable("/Applications/Games/Alpha/Gone.app", [], _install));

        Assert.Equal("/Applications/Games/Alpha/Gone.app", error.Args["path"]);
        Assert.Equal(RepoRole.Install, error.Args["role"]);
        Assert.Empty(starter.Requests);
    }

    [Fact]
    public void A_bare_executable_with_an_execute_bit_starts_directly()
    {
        var (launcher, starter, _) = Build();

        launcher.StartExecutable(_binary, ["-x"], _install);

        Assert.Equal(new ProcessStartRequest(_binary, ["-x"], _install, UseShellExecute: false), starter.Only, new RequestComparer());
    }

    [Fact]
    public void A_bare_file_without_an_execute_bit_is_refused()
    {
        var (launcher, starter, _) = Build(files => files.WithFile("/Applications/Games/Alpha/plain"));

        var error = Assert.Throws<LaunchSpawnException>(() => launcher.StartExecutable("/Applications/Games/Alpha/plain", [], _install));

        Assert.Equal(LaunchMethod.Exe, error.Args["method"]);
        Assert.Empty(starter.Requests);
    }

    [Fact]
    public void A_shell_script_runs_through_bin_sh()
    {
        var (launcher, starter, _) = Build(files => files.WithFile("/Applications/Games/Alpha/run.sh"));

        launcher.StartExecutable("/Applications/Games/Alpha/run.sh", ["go"], _install);

        Assert.Equal(new ProcessStartRequest("/bin/sh", ["/Applications/Games/Alpha/run.sh", "go"], _install, UseShellExecute: false), starter.Only, new RequestComparer());
    }

    [Fact]
    public void A_Windows_program_is_refused_with_its_own_error()
    {
        var (launcher, starter, _) = Build(files => files.WithFile("/Applications/Games/Alpha/Game.exe", executable: true));

        var error = Assert.Throws<LaunchWindowsProgramException>(() => launcher.StartExecutable("/Applications/Games/Alpha/Game.exe", [], _install));

        Assert.Equal("/Applications/Games/Alpha/Game.exe", error.Args["path"]);
        Assert.Empty(starter.Requests);
    }

    [Fact]
    public async Task A_link_goes_to_open_as_its_only_argument()
    {
        var (launcher, starter, _) = Build();
        var opener = new ScriptedProcess();
        opener.Exit(0);
        starter.Answer = _ => opener;

        await launcher.OpenUriAsync("steam://run/489830", Ct);

        Assert.Equal(new ProcessStartRequest("/usr/bin/open", ["steam://run/489830"], null, UseShellExecute: false), starter.Only, new RequestComparer());
    }

    [Fact]
    public async Task A_link_open_cannot_handle_is_a_uri_spawn_failure()
    {
        var (launcher, starter, _) = Build();
        var opener = new ScriptedProcess();
        opener.Exit(1);
        starter.Answer = _ => opener;

        var error = await Assert.ThrowsAsync<LaunchSpawnException>(() => launcher.OpenUriAsync("steam://run/489830", Ct));

        Assert.Equal(LaunchMethod.Uri, error.Args["method"]);
        Assert.Contains("exit code 1", error.Message);
    }
}
