using System.Globalization;
using Savepoint.Testing;

namespace Savepoint.Core.Tests.Build;

// Runs copies of the gate scripts against a stand-in for dotnet that records its calls and writes the reports a test process would, so
// the split of the App tests into processes is checked end to end without building or testing anything.
public sealed class GateSplitTests : IDisposable
{
    private const string _fakeScript = """
        $calls = Join-Path $PSScriptRoot 'calls'
        New-Item -ItemType Directory -Force -Path $calls | Out-Null
        Set-Content -LiteralPath (Join-Path $calls ([guid]::NewGuid().ToString('N') + '.txt')) -Value ($args -join ' ')
        if ($args[0] -ne 'test') { exit 0 }
        if ($args -contains '--list-tests') {
            'Running tests from fake.dll'
            foreach ($method in 1..[int]$env:FAKE_LISTED) {
                # The first method is a theory: three lines in a report, one in a listing.
                "  Fake.Tests.Method$method" + $(if ($method -eq 1) { '(row: 0)' } else { '' })
            }
            'Discovered many tests.'
            exit 0
        }
        $name = $args[[array]::IndexOf($args, '--report-xunit-filename') + 1]
        $directory = $args[[array]::IndexOf($args, '--results-directory') + 1]
        $process = [IO.Path]::GetFileNameWithoutExtension($name)
        $total = 0
        foreach ($pair in $env:FAKE_TOTALS -split ',') {
            $key, $value = $pair -split '='
            if ($key -eq $process) { $total = [int]$value }
        }
        $tests = foreach ($method in 1..$total) {
            foreach ($row in 1..$(if ($method -eq 1) { 3 } else { 1 })) { "<test name=`"Fake.$process.Method$method(row: $row)`" result=`"Pass`" type=`"Fake.$process`" method=`"Method$method`" />" }
        }
        Set-Content -LiteralPath (Join-Path $directory $name) -Value "<?xml version=`"1.0`"?><assemblies><assembly name=`"x`">$($tests -join '')</assembly></assemblies>"
        if ($env:FAKE_FAILING -eq $process) { 'a test failed'; exit 2 }
        exit 0
        """;

    private readonly string _root = Directory.CreateTempSubdirectory("savepoint-gate-split-").FullName;

    public enum Script
    {
        PowerShell,
        Bash,
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static string RepoRoot => TestEnvironment.RepoRoot ?? throw new InvalidOperationException("These tests read the repository's build files.");

    private string FakeFolder => Path.Combine(_root, "fake");

    private sealed record Gate(int ExitCode, string Output, IReadOnlyList<string> Calls);

    private sealed record TestProcess(string Name, IReadOnlyList<string> Arguments);

    private async Task<bool> PrepareAsync()
    {
        var build = Path.Combine(_root, "build");
        Directory.CreateDirectory(build);
        foreach (var file in new[] { "gate.ps1", "gate.sh", "app-test-lanes.txt", "check-test-counts.ps1", "check-test-counts.sh" })
        {
            File.Copy(Path.Combine(RepoRoot, "build", file), Path.Combine(build, file));
        }

        Directory.CreateDirectory(FakeFolder);
        await File.WriteAllTextAsync(Path.Combine(FakeFolder, "fake-dotnet.ps1"), _fakeScript, TestContext.Current.CancellationToken);
        // The gate asks git for the folder of its lock; a repository of its own keeps the lock out of the real checkout.
        var init = await ScriptProcess.RunAsync("git", ["init", "-q"], _root);
        if (init is null)
        {
            return false;
        }

        Assert.True(init.ExitCode == 0, init.Output);

        var unixWrapper = Path.Combine(FakeFolder, "dotnet");
        await File.WriteAllTextAsync(unixWrapper, "#!/bin/sh\nexec pwsh -NoProfile -File \"$(dirname \"$0\")/fake-dotnet.ps1\" \"$@\"\n", TestContext.Current.CancellationToken);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(unixWrapper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        await File.WriteAllTextAsync(Path.Combine(FakeFolder, "dotnet.cmd"), "@pwsh -NoProfile -File \"%~dp0fake-dotnet.ps1\" %*\r\n", TestContext.Current.CancellationToken);
        return true;
    }

    private const int _laneTests = 2;
    private const int _restTests = 3;

    // Each lane's process reports _laneTests tests and the rest process _restTests, so the full listing holds that sum; listedMore makes the listing disagree.
    private static int ListedTests(int listedMore = 0) => (Lanes().Count * _laneTests) + _restTests + listedMore;

    private async Task<Gate?> RunGateAsync(Script script, IEnumerable<string> gateArguments, int listedMore = 0, string? failing = null, string testArgs = "")
    {
        if (!await PrepareAsync())
        {
            return null;
        }

        var totals = string.Join(',', Lanes().Select(lane => $"app-{lane.Name}={_laneTests}").Append($"app-rest={_restTests}").Append("core=50"));
        var environment = new Dictionary<string, string>
        {
            ["PATH"] = FakeFolder + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"),
            ["FAKE_LISTED"] = ListedTests(listedMore).ToString(CultureInfo.InvariantCulture),
            ["FAKE_TOTALS"] = totals,
            ["FAKE_FAILING"] = failing ?? string.Empty,
            ["SAVEPOINT_TEST_ARGS"] = testArgs,
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
            ["GIT_CEILING_DIRECTORIES"] = Path.GetDirectoryName(_root)!,
        };
        ScriptProcess.Result? run;
        if (script == Script.PowerShell)
        {
            List<string> arguments = ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(_root, "build", "gate.ps1")];
            arguments.AddRange(gateArguments);
            run = await ScriptProcess.RunAsync("pwsh", arguments, _root, environment);
        }
        else
        {
            run = await ScriptProcess.RunAsync(ScriptProcess.BashProgram(), [ScriptProcess.ForBash(Path.Combine(_root, "build", "gate.sh")), .. gateArguments], _root, environment);
        }

        if (run is null)
        {
            return null;
        }

        var callsFolder = Path.Combine(FakeFolder, "calls");
        var calls = Directory.Exists(callsFolder)
            ? Directory.GetFiles(callsFolder).Select(file => File.ReadAllText(file).Trim()).ToList()
            : [];
        return new Gate(run.ExitCode, run.Output, calls);
    }

    // The test processes of a run: every call that asked for an xunit report, by the name of its report.
    private static List<TestProcess> TestProcesses(Gate gate) =>
        [.. gate.Calls.Where(call => call.StartsWith("test ", StringComparison.Ordinal) && call.Contains("--report-xunit-filename ", StringComparison.Ordinal))
            .Select(call =>
            {
                var words = call.Split(' ');
                var name = words[Array.IndexOf(words, "--report-xunit-filename") + 1];
                return new TestProcess(name.Replace(".xunit", string.Empty, StringComparison.Ordinal), words);
            })
            .OrderBy(process => process.Name, StringComparer.Ordinal)];

    private static string[] Values(TestProcess process, string option) =>
        [.. process.Arguments.Select((word, index) => (word, index)).Where(pair => pair.word == option).Select(pair => process.Arguments[pair.index + 1])];

    private static List<(string Name, string Namespace)> Lanes() =>
        [.. File.ReadAllLines(Path.Combine(RepoRoot, "build", "app-test-lanes.txt")).Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Select(words => (words[0], words[1]))];

    [Theory]
    [InlineData(Script.PowerShell)]
    [InlineData(Script.Bash)]
    public async Task The_gate_runs_core_and_each_app_lane_as_its_own_process_with_the_lanes_filters(Script script)
    {
        var gate = await RunGateAsync(script, [], testArgs: "--report-trx");

        Assert.SkipWhen(gate is null, "pwsh, bash or git is not installed here.");
        Assert.True(gate!.ExitCode == 0, gate.Output);
        var processes = TestProcesses(gate);
        var lanes = Lanes();
        Assert.Equal(
            new[] { "core" }.Concat(lanes.Select(lane => "app-" + lane.Name)).Append("app-rest").Order(StringComparer.Ordinal),
            processes.Select(process => process.Name));

        var core = processes.Single(process => process.Name == "core");
        Assert.Contains("tests/Savepoint.Core.Tests", core.Arguments);
        Assert.Empty(Values(core, "--filter-namespace"));
        Assert.Empty(Values(core, "--filter-not-namespace"));

        foreach (var (name, ns) in lanes)
        {
            var lane = processes.Single(process => process.Name == "app-" + name);
            Assert.Contains("tests/Savepoint.App.Tests", lane.Arguments);
            Assert.Equal([ns], Values(lane, "--filter-namespace"));
            Assert.Empty(Values(lane, "--filter-not-namespace"));
        }

        var rest = processes.Single(process => process.Name == "app-rest");
        Assert.Contains("tests/Savepoint.App.Tests", rest.Arguments);
        Assert.Empty(Values(rest, "--filter-namespace"));
        Assert.Equal(lanes.Select(lane => lane.Namespace).Order(StringComparer.Ordinal), Values(rest, "--filter-not-namespace").Order(StringComparer.Ordinal));

        Assert.All(processes, process =>
        {
            Assert.Contains("--report-xunit", process.Arguments);
            Assert.Contains("--report-trx", process.Arguments);
            Assert.Contains("--max-threads", process.Arguments);
            Assert.DoesNotContain("--retry-failed-tests", process.Arguments);
        });
        Assert.Contains($"= {ListedTests()} test methods, as many as the full listing", gate.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Script.PowerShell)]
    [InlineData(Script.Bash)]
    public async Task The_gate_fails_when_the_lanes_ran_fewer_tests_than_the_full_listing_holds(Script script)
    {
        var gate = await RunGateAsync(script, [], listedMore: 1);

        Assert.SkipWhen(gate is null, "pwsh, bash or git is not installed here.");
        Assert.NotEqual(0, gate!.ExitCode);
        Assert.Contains("test count check failed", gate.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(gate.Calls, call => call.StartsWith("format", StringComparison.Ordinal) || call.StartsWith("run ", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(Script.PowerShell)]
    [InlineData(Script.Bash)]
    public async Task The_gate_fails_and_names_the_process_that_failed(Script script)
    {
        var failing = "app-" + Lanes()[^1].Name;
        var gate = await RunGateAsync(script, [], failing: failing);

        Assert.SkipWhen(gate is null, "pwsh, bash or git is not installed here.");
        Assert.NotEqual(0, gate!.ExitCode);
        Assert.Contains($"test failed in: {failing}", gate.Output.Replace("'", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.DoesNotContain("test count check", gate.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(gate.Calls, call => call.StartsWith("format", StringComparison.Ordinal) || call.StartsWith("run ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_windows_gate_checks_the_format_unless_it_is_told_to_skip_it()
    {
        var gate = await RunGateAsync(Script.PowerShell, []);

        Assert.SkipWhen(gate is null, "pwsh or git is not installed here.");
        Assert.True(gate!.ExitCode == 0, gate.Output);
        Assert.Contains("format Savepoint.slnx --verify-no-changes", gate.Calls);
    }

    [Fact]
    public async Task The_windows_gate_leaves_out_the_format_check_with_skip_format_and_still_runs_the_rest()
    {
        var gate = await RunGateAsync(Script.PowerShell, ["-SkipFormat"]);

        Assert.SkipWhen(gate is null, "pwsh or git is not installed here.");
        Assert.True(gate!.ExitCode == 0, gate.Output);
        Assert.DoesNotContain(gate.Calls, call => call.StartsWith("format", StringComparison.Ordinal));
        Assert.Equal(Lanes().Count + 2, TestProcesses(gate).Count);
        Assert.Contains(gate.Calls, call => call.StartsWith("run --project tools/SkipCheck", StringComparison.Ordinal));
        Assert.Contains("format: skipped", gate.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_linux_gate_always_checks_the_format()
    {
        var gate = await RunGateAsync(Script.Bash, []);

        Assert.SkipWhen(gate is null, "pwsh, bash or git is not installed here.");
        Assert.True(gate!.ExitCode == 0, gate.Output);
        Assert.Contains("format Savepoint.slnx --verify-no-changes", gate.Calls);
    }
}
