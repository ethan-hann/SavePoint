using System.Diagnostics;
using Savepoint.App.Platform.Processes;

namespace Savepoint.App.Tests.Platform;

// These start real but harmless programs that every machine ships: cmd on Windows, sh elsewhere.
public class SystemProcessRunnerTests
{
    private static readonly TimeSpan _generous = TimeSpan.FromSeconds(30);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ProcessCommand Shell(string script) =>
        OperatingSystem.IsWindows()
            ? new ProcessCommand("cmd.exe", ["/c", script])
            : new ProcessCommand("/bin/sh", ["-c", script]);

    [Fact]
    public async Task Output_and_exit_code_come_back_from_a_finished_program()
    {
        var runner = new SystemProcessRunner();

        var output = await runner.RunAsync(Shell(OperatingSystem.IsWindows() ? "echo hello&exit 3" : "echo hello; exit 3"), _generous, Ct);

        Assert.NotNull(output);
        Assert.Equal(3, output.ExitCode);
        Assert.Equal("hello", output.StandardOutput.Trim());
    }

    [Fact]
    public async Task A_program_that_does_not_exist_gives_null()
    {
        var runner = new SystemProcessRunner();

        var output = await runner.RunAsync(new ProcessCommand("savepoint-no-such-program", []), _generous, Ct);

        Assert.Null(output);
    }

    [Fact]
    public async Task A_program_that_outlives_the_timeout_is_killed_and_gives_null()
    {
        var runner = new SystemProcessRunner();
        var sleep = OperatingSystem.IsWindows() ? "ping -n 60 127.0.0.1 >nul" : "sleep 60";
        var watch = Stopwatch.StartNew();

        var output = await runner.RunAsync(Shell(sleep), TimeSpan.FromMilliseconds(300), Ct);

        Assert.Null(output);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20), $"the wait lasted {watch.Elapsed}");
    }

    [Fact]
    public async Task The_killed_program_never_gets_to_its_next_step()
    {
        var runner = new SystemProcessRunner();
        var folder = Path.Combine(Path.GetTempPath(), "savepoint-kill-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var markerPath = Path.Combine(folder, "marker.tmp");
        var controlPath = Path.Combine(folder, "control.tmp");

        // The path travels as its own argument, so the runner quotes it and a folder with spaces in its name still works; cmd mangles quotes written into a script.
        var script = OperatingSystem.IsWindows()
            ? new ProcessCommand("cmd.exe", ["/c", "ping", "-n", "4", "127.0.0.1", ">nul", "&", "echo", "x", ">", markerPath])
            : new ProcessCommand("/bin/sh", ["-c", "sleep 3; touch \"$1\"", "sh", markerPath]);
        var control = OperatingSystem.IsWindows()
            ? new ProcessCommand("cmd.exe", ["/c", "echo", "x", ">", controlPath])
            : new ProcessCommand("/bin/sh", ["-c", "touch \"$1\"", "sh", controlPath]);

        try
        {
            // Without this a script that cannot write its marker would pass the test for the wrong reason.
            await runner.RunAsync(control, _generous, Ct);
            Assert.True(File.Exists(controlPath), "the script cannot write a marker into the test's folder");

            var output = await runner.RunAsync(script, TimeSpan.FromMilliseconds(300), Ct);
            await Task.Delay(TimeSpan.FromSeconds(5), Ct);

            Assert.Null(output);
            Assert.False(File.Exists(markerPath), "the program kept running after the timeout");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task A_canceled_run_kills_the_program_and_gives_null()
    {
        var runner = new SystemProcessRunner();
        var sleep = OperatingSystem.IsWindows() ? "ping -n 60 127.0.0.1 >nul" : "sleep 60";
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cancel.CancelAfter(TimeSpan.FromMilliseconds(300));
        var watch = Stopwatch.StartNew();

        var output = await runner.RunAsync(Shell(sleep), _generous, cancel.Token);

        Assert.Null(output);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20), $"the wait lasted {watch.Elapsed}");
    }

    [Fact]
    public void A_program_that_does_not_exist_cannot_be_started()
    {
        var runner = new SystemProcessRunner();

        Assert.False(runner.TryStart(new ProcessCommand("savepoint-no-such-program", [])));
    }

    [Fact]
    public void A_program_that_exists_is_started_without_waiting_for_it()
    {
        var runner = new SystemProcessRunner();

        Assert.True(runner.TryStart(Shell("exit 0")));
    }

    [Fact]
    public async Task Each_argument_arrives_as_one_item_whatever_it_contains()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "sh is the program that prints its arguments back; Windows has no equivalent that skips command-line re-parsing.");
        var runner = new SystemProcessRunner();
        string[] args = ["two words", "a\"quote", "$HOME", "semi;colon", "-dash"];

        var output = await runner.RunAsync(new ProcessCommand("/bin/sh", ["-c", "for a in \"$@\"; do printf '[%s]' \"$a\"; done", "sh", .. args]), _generous, Ct);

        Assert.NotNull(output);
        Assert.Equal("[two words][a\"quote][$HOME][semi;colon][-dash]", output.StandardOutput);
    }
}
