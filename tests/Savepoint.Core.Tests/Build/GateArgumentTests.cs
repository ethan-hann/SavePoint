using System.Diagnostics;
using Savepoint.Testing;

namespace Savepoint.Core.Tests.Build;

// The gate scripts refuse test arguments that could run fewer tests or let a failure pass. Each run uses a copy of the script in an
// empty folder, so an argument that is let through reaches a build with nothing to build and fails there, never in a real build.
public sealed class GateArgumentTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("savepoint-gate-args-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static string RepoRoot => TestEnvironment.RepoRoot ?? throw new InvalidOperationException("These tests read the repository's build files.");

    // Runs a copy of the gate script with SAVEPOINT_TEST_ARGS set, and returns its exit code and everything it printed.
    private async Task<(int ExitCode, string Output)> RunGateAsync(string script, string testArgs)
    {
        var scripts = Path.Combine(_root, "build");
        Directory.CreateDirectory(scripts);
        File.Copy(Path.Combine(RepoRoot, "build", script), Path.Combine(scripts, script), overwrite: true);

        var info = script.EndsWith(".ps1", StringComparison.Ordinal)
            ? new ProcessStartInfo("pwsh") { ArgumentList = { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(scripts, script) } }
            : new ProcessStartInfo("bash") { ArgumentList = { Path.Combine(scripts, script) } };
        info.UseShellExecute = false;
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        info.WorkingDirectory = _root;
        info.Environment["SAVEPOINT_TEST_ARGS"] = testArgs;
        info.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";

        Process? process = null;
        try
        {
            process = Process.Start(info);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Not every machine has the program on its path.
        }

        Assert.SkipWhen(process is null, info.FileName + " is not installed here.");
        using (process)
        {
            var output = process!.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);
            return (process.ExitCode, await output + await error);
        }
    }

    public enum RetrySpelling
    {
        Bare,
        WithCountAfterIt,
        WithCountAfterEquals,
        WithMaxPercentage,
    }

    private static string Spell(RetrySpelling spelling) => spelling switch
    {
        RetrySpelling.Bare => "--retry-failed-tests",
        RetrySpelling.WithCountAfterIt => "--retry-failed-tests 2",
        RetrySpelling.WithCountAfterEquals => "--retry-failed-tests=2",
        _ => "--retry-failed-tests-max-percentage 20",
    };

    [Theory]
    [InlineData(RetrySpelling.Bare)]
    [InlineData(RetrySpelling.WithCountAfterIt)]
    [InlineData(RetrySpelling.WithCountAfterEquals)]
    [InlineData(RetrySpelling.WithMaxPercentage)]
    public async Task The_windows_gate_refuses_a_retry_of_failed_tests(RetrySpelling spelling)
    {
        var testArgs = Spell(spelling);
        var run = await RunGateAsync("gate.ps1", testArgs);

        Assert.True(run.Output.Contains("gate refused", StringComparison.Ordinal) && run.Output.Contains("--retry-failed-tests", StringComparison.Ordinal), $"the gate did not refuse {testArgs}:\n{run.Output}");
        Assert.Equal(1, run.ExitCode);
    }

    [Theory]
    [InlineData(RetrySpelling.Bare)]
    [InlineData(RetrySpelling.WithCountAfterIt)]
    [InlineData(RetrySpelling.WithCountAfterEquals)]
    [InlineData(RetrySpelling.WithMaxPercentage)]
    public async Task The_linux_gate_refuses_a_retry_of_failed_tests(RetrySpelling spelling)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "On Windows the bash on the path may be a different system's, so the bash script is tested where bash is the shell.");

        var testArgs = Spell(spelling);
        var run = await RunGateAsync("gate.sh", testArgs);

        Assert.True(run.Output.Contains("gate refused", StringComparison.Ordinal) && run.Output.Contains("--retry-failed-tests", StringComparison.Ordinal), $"the gate did not refuse {testArgs}:\n{run.Output}");
        Assert.Equal(1, run.ExitCode);
    }

    [Fact]
    public async Task The_windows_gate_does_not_refuse_a_report()
    {
        var run = await RunGateAsync("gate.ps1", "--report-trx");

        Assert.DoesNotContain("gate refused", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_linux_gate_does_not_refuse_a_report()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "On Windows the bash on the path may be a different system's, so the bash script is tested where bash is the shell.");

        var run = await RunGateAsync("gate.sh", "--report-trx");

        Assert.DoesNotContain("gate refused", run.Output, StringComparison.Ordinal);
    }
}
