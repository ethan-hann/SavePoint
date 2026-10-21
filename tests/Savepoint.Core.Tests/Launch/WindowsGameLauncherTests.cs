using System.ComponentModel;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Launch;

namespace Savepoint.Core.Tests.Launch;

public class WindowsGameLauncherTests
{
    private const string _install = @"C:\Games\Alpha";
    private const string _exe = @"C:\Games\Alpha\Alpha.exe";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (WindowsGameLauncher Launcher, RecordingStarter Starter) Build(bool exeExists = true)
    {
        var files = new FakeLaunchFiles();
        if (exeExists)
        {
            files.WithFile(_exe);
        }

        var starter = new RecordingStarter();
        return (new WindowsGameLauncher(starter, files), starter);
    }

    [Fact]
    public void An_exe_starts_without_a_shell_in_the_install_folder_with_the_arguments_as_a_list()
    {
        var (launcher, starter) = Build();

        launcher.StartExecutable(_exe, ["-windowed", "-skipintro"], _install);

        Assert.Equal(new ProcessStartRequest(_exe, ["-windowed", "-skipintro"], _install, UseShellExecute: false), starter.Only, new RequestComparer());
    }

    [Fact]
    public void Arguments_with_spaces_and_quotes_stay_one_item_each_and_are_not_quoted_by_the_launcher()
    {
        var (launcher, starter) = Build();

        launcher.StartExecutable(_exe, ["--name", "two words", "say \"hi\"", ""], _install);

        Assert.Equal(["--name", "two words", "say \"hi\"", ""], starter.Only.Arguments);
    }

    [Fact]
    public void The_caller_gets_the_process_the_starter_returned()
    {
        var (launcher, starter) = Build();
        var process = new ScriptedProcess(77);
        starter.Answer = _ => process;

        var started = launcher.StartExecutable(_exe, [], _install);

        Assert.Same(process, started);
    }

    [Fact]
    public void A_missing_exe_is_reported_as_a_missing_install_path_and_nothing_is_started()
    {
        var (launcher, starter) = Build(exeExists: false);

        var error = Assert.Throws<PathMissingException>(() => launcher.StartExecutable(_exe, [], _install));

        Assert.Equal(_exe, error.Args["path"]);
        Assert.Equal(RepoRole.Install, error.Args["role"]);
        Assert.Empty(starter.Requests);
    }

    [Theory]
    [InlineData("elevation")]
    [InlineData("no file name")]
    [InlineData("io")]
    [InlineData("access")]
    public void A_start_the_OS_refuses_becomes_an_exe_spawn_failure_that_keeps_the_cause(string failure)
    {
        Exception cause = failure switch
        {
            "elevation" => new Win32Exception(740, "The requested operation requires elevation."),
            "no file name" => new InvalidOperationException("No file name was specified."),
            "io" => new IOException("The device is not ready."),
            _ => new UnauthorizedAccessException("Access is denied."),
        };
        var (launcher, starter) = Build();
        starter.Answer = _ => throw cause;

        var error = Assert.Throws<LaunchSpawnException>(() => launcher.StartExecutable(_exe, [], _install));

        Assert.Equal(LaunchMethod.Exe, error.Args["method"]);
        Assert.Same(cause, error.InnerException);
        Assert.Contains(_exe, error.Message);
        Assert.Contains(cause.Message, error.Message);
    }

    [Fact]
    public void A_starter_that_gives_back_no_process_is_a_spawn_failure()
    {
        var (launcher, starter) = Build();
        starter.Answer = _ => null;

        var error = Assert.Throws<LaunchSpawnException>(() => launcher.StartExecutable(_exe, [], _install));

        Assert.Equal(LaunchMethod.Exe, error.Args["method"]);
    }

    [Fact]
    public async Task A_link_goes_to_the_shell_as_the_file_name_with_no_arguments()
    {
        var (launcher, starter) = Build();

        await launcher.OpenUriAsync("steam://run/489830", Ct);

        Assert.Equal(new ProcessStartRequest("steam://run/489830", [], null, UseShellExecute: true), starter.Only, new RequestComparer());
    }

    [Fact]
    public async Task A_process_the_shell_hands_back_for_a_link_is_released_without_being_stopped()
    {
        var (launcher, starter) = Build();
        var process = new ScriptedProcess();
        starter.Answer = _ => process;

        await launcher.OpenUriAsync("steam://run/489830", Ct);

        Assert.True(process.Disposed);
        Assert.False(process.HasExited);
    }

    [Fact]
    public async Task A_link_nobody_handles_becomes_a_uri_spawn_failure_that_keeps_the_cause()
    {
        var (launcher, starter) = Build();
        var cause = new Win32Exception(1155, "No application is associated with the specified file for this operation.");
        starter.Answer = _ => throw cause;

        var error = await Assert.ThrowsAsync<LaunchSpawnException>(() => launcher.OpenUriAsync("steam://run/489830", Ct));

        Assert.Equal(LaunchMethod.Uri, error.Args["method"]);
        Assert.Same(cause, error.InnerException);
        Assert.Contains("steam://run/489830", error.Message);
    }

    [Fact]
    public async Task A_link_the_shell_took_without_giving_a_process_is_a_success()
    {
        var (launcher, starter) = Build();
        starter.Answer = _ => null;

        await launcher.OpenUriAsync("steam://run/489830", Ct);

        Assert.Single(starter.Requests);
    }
}

// The request holds a list, which record equality would compare by reference.
internal sealed class RequestComparer : IEqualityComparer<ProcessStartRequest>
{
    public bool Equals(ProcessStartRequest? x, ProcessStartRequest? y) =>
        x is not null && y is not null
        && x.FileName == y.FileName
        && x.WorkingDirectory == y.WorkingDirectory
        && x.UseShellExecute == y.UseShellExecute
        && x.Arguments.SequenceEqual(y.Arguments);

    public int GetHashCode(ProcessStartRequest obj) => obj.FileName.GetHashCode(StringComparison.Ordinal);
}
