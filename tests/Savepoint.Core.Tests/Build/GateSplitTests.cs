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
        # Where a test process would make its temp files, and whether that folder is there when it starts.
        if ($args -notcontains '--list-tests') {
            $tmp = Join-Path $PSScriptRoot 'tmpdirs'
            New-Item -ItemType Directory -Force -Path $tmp | Out-Null
            Set-Content -LiteralPath (Join-Path $tmp ([guid]::NewGuid().ToString('N') + '.txt')) -Value "$env:TMPDIR|$(if ($env:TMPDIR) { Test-Path -LiteralPath $env:TMPDIR } else { $false })"
        }
        if ($args -contains '--list-tests') {
            'Running tests from fake.dll'
            foreach ($method in 1..[int]$env:FAKE_LISTED) {
                # The first method is a theory: three lines in a report, one in a listing.
                "  Fake.Tests.Method$method" + $(if ($method -eq 1) { '(row: 0)' } else { '' })
            }
            # A method whose name differs from another's only in case is a different method.
            if ($env:FAKE_CASE_VARIANT) { '  Fake.Tests.METHOD2' }
            'Discovered many tests.'
            exit 0
        }
        $name = $args[[array]::IndexOf($args, '--report-xunit-filename') + 1]
        $directory = $args[[array]::IndexOf($args, '--results-directory') + 1]
        $process = [IO.Path]::GetFileNameWithoutExtension($name)
        "output of $process, first line"
        if ($env:FAKE_SLOW -eq $process) { Start-Sleep -Seconds 4 }
        "output of $process, last line"
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

    // TempFolders: the TMPDIR each test process saw when it started, and whether that folder existed then.
    private sealed record Gate(int ExitCode, string Output, IReadOnlyList<string> Calls, IReadOnlyList<(string Path, bool Existed)> TempFolders);

    private sealed record TestProcess(string Name, IReadOnlyList<string> Arguments);

    private async Task<bool> PrepareAsync()
    {
        var build = Path.Combine(_root, "build");
        Directory.CreateDirectory(build);
        foreach (var file in new[] { "gate.ps1", "gate.sh", "app-test-lanes.txt", "check-test-counts.ps1", "check-test-counts.sh" })
        {
            File.Copy(Path.Combine(RepoRoot, "build", file), Path.Combine(build, file));
        }

        await WriteStandInAsync(RealDotnet());
        // The gate asks git for the folder of its lock; a repository of its own keeps the lock out of the real checkout.
        var init = await ScriptProcess.RunAsync("git", ["init", "-q"], _root);
        if (init is null)
        {
            return false;
        }

        Assert.True(init.ExitCode == 0, init.Output);
        return true;
    }

    // The SDK's own dotnet, found from the runtime this test host runs on, so it never depends on what the stand-in hides on PATH.
    private static string RealDotnet()
    {
        var program = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        var coreLib = typeof(object).Assembly.Location;
        // <dotnet>/shared/Microsoft.NETCore.App/<version>/System.Private.CoreLib.dll
        var root = coreLib.Length == 0 ? null : Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(coreLib))));
        if (root is not null && File.Exists(Path.Combine(root, program)))
        {
            return Path.Combine(root, program);
        }

        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (File.Exists(Path.Combine(folder, program)))
            {
                return Path.Combine(folder, program);
            }
        }

        throw new InvalidOperationException("The real dotnet was not found, so the stand-in cannot hand PowerShell's own starts to it.");
    }

    // The stand-in dotnet of the gate tests. PowerShell installed as a .NET tool is a shim that starts "dotnet <path>/pwsh.dll" from PATH,
    // which finds this stand-in first, so those starts go to the real dotnet. Without that, the stand-in starts pwsh, the shim starts the
    // stand-in again, and processes pile up until the machine runs out of memory. The depth counter stops any other way of calling itself.
    private async Task WriteStandInAsync(string realDotnet)
    {
        Directory.CreateDirectory(FakeFolder);
        await File.WriteAllTextAsync(Path.Combine(FakeFolder, "fake-dotnet.ps1"), _fakeScript, TestContext.Current.CancellationToken);

        var unixWrapper = Path.Combine(FakeFolder, "dotnet");
        var script = $$"""
            #!/bin/sh
            case "$1" in
              *pwsh.dll) exec "{{ScriptProcess.ForBash(realDotnet)}}" "$@" ;;
            esac
            depth="${SAVEPOINT_STANDIN_DEPTH:-0}"
            if [ "$depth" -ge {{_standInDepthLimit}} ]; then
              echo "The stand-in dotnet was started from itself $depth times in a row and stops here." >&2
              exit 97
            fi
            SAVEPOINT_STANDIN_DEPTH=$((depth + 1))
            export SAVEPOINT_STANDIN_DEPTH
            exec pwsh -NoProfile -File "$(dirname "$0")/fake-dotnet.ps1" "$@"

            """.ReplaceLineEndings("\n");
        await File.WriteAllTextAsync(unixWrapper, script, TestContext.Current.CancellationToken);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(unixWrapper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        await File.WriteAllTextAsync(Path.Combine(FakeFolder, "dotnet.cmd"), "@pwsh -NoProfile -File \"%~dp0fake-dotnet.ps1\" %*\r\n", TestContext.Current.CancellationToken);
    }

    // The gate runs the stand-in once for each dotnet command; nothing it starts calls dotnet again, so a deeper start is a loop.
    private const int _standInDepthLimit = 3;

    private const int _laneTests = 2;
    private const int _restTests = 3;

    // Each lane's process reports _laneTests tests and the rest process _restTests, so the full listing holds that sum; listedMore makes the listing disagree.
    private static int ListedTests(int listedMore = 0) => (Lanes().Count * _laneTests) + _restTests + listedMore;

    // The least available memory, in MB, at which the gate runs the App tests in lanes; the gate scripts hold the same number.
    private const int _lanesMemoryMegabytes = 10240;

    private static Dictionary<string, string> Memory(int megabytes) =>
        new() { ["SAVEPOINT_GATE_AVAILABLE_MEMORY_MB"] = megabytes.ToString(CultureInfo.InvariantCulture) };

    // A machine with room to spare, so a test that is not about memory runs the lanes whatever this machine has free.
    private static Dictionary<string, string> RoomToSpare() => Memory(65536);

    private async Task<Gate?> RunGateAsync(Script script, IEnumerable<string> gateArguments, int listedMore = 0, string? failing = null, string testArgs = "", IReadOnlyDictionary<string, string>? settings = null)
    {
        if (!await PrepareAsync())
        {
            return null;
        }

        // The whole App run as one process reports every test method it lists.
        var totals = string.Join(',', Lanes().Select(lane => $"app-{lane.Name}={_laneTests}").Append($"app-rest={_restTests}").Append("core=50").Append($"app={ListedTests()}"));
        var environment = new Dictionary<string, string>
        {
            ["PATH"] = FakeFolder + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"),
            ["FAKE_LISTED"] = ListedTests(listedMore).ToString(CultureInfo.InvariantCulture),
            ["FAKE_TOTALS"] = totals,
            ["FAKE_FAILING"] = failing ?? string.Empty,
            ["SAVEPOINT_TEST_ARGS"] = testArgs,
            ["SAVEPOINT_GATE_TEST_THREADS"] = string.Empty,
            ["SAVEPOINT_GATE_APP_LANES"] = string.Empty,
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
            ["GIT_CEILING_DIRECTORIES"] = Path.GetDirectoryName(_root)!,
        };
        foreach (var (name, value) in settings ?? RoomToSpare())
        {
            environment[name] = value;
        }

        if (!environment.ContainsKey("SAVEPOINT_GATE_AVAILABLE_MEMORY_MB"))
        {
            environment["SAVEPOINT_GATE_AVAILABLE_MEMORY_MB"] = RoomToSpare()["SAVEPOINT_GATE_AVAILABLE_MEMORY_MB"];
        }

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
        var tempFolders = Path.Combine(FakeFolder, "tmpdirs");
        var temps = Directory.Exists(tempFolders)
            ? Directory.GetFiles(tempFolders).Select(file => File.ReadAllText(file).Trim().Split('|')).Select(parts => (parts[0], string.Equals(parts[1], "True", StringComparison.OrdinalIgnoreCase))).ToList()
            : [];
        return new Gate(run.ExitCode, run.Output, calls, temps);
    }

    // Runs the stand-in's own script, the way a shell on PATH would, and returns what it printed; null when bash is not installed here.
    private async Task<ScriptProcess.Result?> RunStandInAsync(string realDotnet, IEnumerable<string> arguments, IReadOnlyDictionary<string, string>? environment = null)
    {
        await WriteStandInAsync(realDotnet);
        return await ScriptProcess.RunAsync(ScriptProcess.BashProgram(), [ScriptProcess.ForBash(Path.Combine(FakeFolder, "dotnet")), .. arguments], _root, environment);
    }

    [Fact]
    public async Task The_stand_in_dotnet_hands_a_start_of_pwsh_dll_to_the_real_dotnet_and_runs_nothing_of_its_own()
    {
        // A PowerShell installed as a .NET tool starts "dotnet <folder>/pwsh.dll ..." through PATH; the real one must answer that.
        var recorded = Path.Combine(_root, "forwarded.txt");
        var real = Path.Combine(_root, "real-dotnet");
        await File.WriteAllTextAsync(real, $"#!/bin/sh\nprintf '%s\\n' \"$*\" > \"{ScriptProcess.ForBash(recorded)}\"\n", TestContext.Current.CancellationToken);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(real, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var run = await RunStandInAsync(real, ["/tools/pwsh.dll", "-NoProfile", "-File", "gate.ps1"]);

        Assert.SkipWhen(run is null, "bash is not installed here.");
        Assert.True(File.Exists(recorded), $"The real dotnet was never started. The stand-in printed: {run!.Output}");
        Assert.Equal("/tools/pwsh.dll -NoProfile -File gate.ps1", (await File.ReadAllTextAsync(recorded, TestContext.Current.CancellationToken)).Trim());
        Assert.False(Directory.Exists(Path.Combine(FakeFolder, "calls")), "The stand-in recorded a call for a start that was PowerShell's own.");
    }

    [Fact]
    public async Task The_stand_in_dotnet_stops_when_it_has_been_started_from_itself_too_many_times_in_a_row()
    {
        var run = await RunStandInAsync(Path.Combine(_root, "unused"), ["test"], new Dictionary<string, string> { ["SAVEPOINT_STANDIN_DEPTH"] = _standInDepthLimit.ToString(CultureInfo.InvariantCulture) });

        Assert.SkipWhen(run is null, "bash is not installed here.");
        Assert.Equal(97, run!.ExitCode);
        Assert.Contains("started from itself", run.Output, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(FakeFolder, "calls")), "The stand-in kept going after its limit.");
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

    private static string[] LaneProcessNames() =>
        [.. new[] { "core" }.Concat(Lanes().Select(lane => "app-" + lane.Name)).Append("app-rest").Order(StringComparer.Ordinal)];

    // The App tests as one process: no namespace filter at all, and the count check reads that process's report alone.
    private static void AssertAppRanAsOneProcess(Gate gate)
    {
        Assert.Equal(["app", "core"], TestProcesses(gate).Select(process => process.Name));
        var app = TestProcesses(gate).Single(process => process.Name == "app");
        Assert.Contains("tests/Savepoint.App.Tests", app.Arguments);
        Assert.Empty(Values(app, "--filter-namespace"));
        Assert.Empty(Values(app, "--filter-not-namespace"));
        Assert.Contains($"test count check: {ListedTests()} = {ListedTests()} test methods, as many as the full listing", gate.Output, StringComparison.Ordinal);
    }

    private static void AssertAppRanInLanes(Gate gate)
    {
        Assert.Equal(LaneProcessNames(), TestProcesses(gate).Select(process => process.Name));
        Assert.Contains($"= {ListedTests()} test methods, as many as the full listing", gate.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Script.PowerShell)]
    [InlineData(Script.Bash)]
    public async Task With_too_little_free_memory_the_gate_runs_the_app_tests_as_one_process_and_says_so(Script script)
    {
        var gate = await RunGateAsync(script, [], settings: Memory(_lanesMemoryMegabytes - 1));

        Assert.SkipWhen(gate is null, "pwsh, bash or git is not installed here.");
        Assert.True(gate!.ExitCode == 0, gate.Output);
        AssertAppRanAsOneProcess(gate);
        Assert.Contains("App tests in one process", gate.Output, StringComparison.Ordinal);
        Assert.Contains($"{_lanesMemoryMegabytes - 1} MB", gate.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Script.PowerShell)]
    [InlineData(Script.Bash)]
    public async Task With_exactly_enough_free_memory_the_gate_runs_the_lanes_and_says_so(Script script)
    {
        var gate = await RunGateAsync(script, [], settings: Memory(_lanesMemoryMegabytes));

        Assert.SkipWhen(gate is null, "pwsh, bash or git is not installed here.");
        Assert.True(gate!.ExitCode == 0, gate.Output);
        AssertAppRanInLanes(gate);
        Assert.Contains("App tests in lanes", gate.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Script.PowerShell)]
    [InlineData(Script.Bash)]
    public async Task One_lane_override_runs_the_app_tests_as_one_process_however_much_memory_is_free(Script script)
    {
        var settings = RoomToSpare();
        settings["SAVEPOINT_GATE_APP_LANES"] = "1";
        var gate = await RunGateAsync(script, [], settings: settings);

        Assert.SkipWhen(gate is null, "pwsh, bash or git is not installed here.");
        Assert.True(gate!.ExitCode == 0, gate.Output);
        AssertAppRanAsOneProcess(gate);
        Assert.Contains("SAVEPOINT_GATE_APP_LANES", gate.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Script.PowerShell)]
    [InlineData(Script.Bash)]
    public async Task A_lanes_override_runs_the_lanes_however_little_memory_is_free(Script script)
    {
        var settings = Memory(1024);
        settings["SAVEPOINT_GATE_APP_LANES"] = "3";
        var gate = await RunGateAsync(script, [], settings: settings);

        Assert.SkipWhen(gate is null, "pwsh, bash or git is not installed here.");
        Assert.True(gate!.ExitCode == 0, gate.Output);
        AssertAppRanInLanes(gate);
        Assert.Contains("SAVEPOINT_GATE_APP_LANES", gate.Output, StringComparison.Ordinal);
    }

    // Settings that make the gate read its memory from fake files, as a Linux machine or container would show them.
    private Dictionary<string, string> FakeMemory(int availableKilobytes, string? cgroupV2 = null, string? cgroupV1 = null, string? cgroupUsage = null)
    {
        var meminfo = Path.Combine(_root, "meminfo");
        File.WriteAllText(meminfo, $"MemTotal:       99999999 kB\nMemFree:         1024 kB\nMemAvailable:   {availableKilobytes} kB\nBuffers:         1024 kB\n");
        var cgroup = Path.Combine(_root, "cgroup");
        Directory.CreateDirectory(Path.Combine(cgroup, "memory"));
        if (cgroupV2 is not null)
        {
            File.WriteAllText(Path.Combine(cgroup, "memory.max"), cgroupV2 + "\n");
        }

        if (cgroupV1 is not null)
        {
            File.WriteAllText(Path.Combine(cgroup, "memory", "memory.limit_in_bytes"), cgroupV1 + "\n");
        }

        if (cgroupUsage is not null)
        {
            // Each version keeps its usage beside its limit; only the one with a limit is read.
            File.WriteAllText(Path.Combine(cgroup, "memory.current"), cgroupUsage + "\n");
            File.WriteAllText(Path.Combine(cgroup, "memory", "memory.usage_in_bytes"), cgroupUsage + "\n");
        }

        return new()
        {
            ["SAVEPOINT_GATE_AVAILABLE_MEMORY_MB"] = string.Empty,
            ["SAVEPOINT_GATE_MEMINFO_FILE"] = meminfo,
            ["SAVEPOINT_GATE_CGROUP_DIR"] = cgroup,
        };
    }

    [Theory]
    [InlineData(Script.PowerShell)]
    [InlineData(Script.Bash)]
    public async Task When_free_memory_cannot_be_read_the_gate_runs_the_app_tests_as_one_process_and_says_so(Script script)
    {
        var settings = FakeMemory(availableKilobytes: 1);
        File.Delete(settings["SAVEPOINT_GATE_MEMINFO_FILE"]);
        var gate = await RunGateAsync(script, [], settings: settings);

        Assert.SkipWhen(gate is null, "pwsh, bash or git is not installed here.");
        Assert.True(gate!.ExitCode == 0, gate.Output);
        AssertAppRanAsOneProcess(gate);
        Assert.Contains("could not be read", gate.Output, StringComparison.Ordinal);
    }

    // 20 GB available in meminfo in every case; the limit of the container decides.
    [Theory]
    [InlineData(Script.PowerShell, "4294967296", null, "one", "4096 MB")]
    [InlineData(Script.Bash, "4294967296", null, "one", "4096 MB")]
    [InlineData(Script.PowerShell, null, "4294967296", "one", "4096 MB")]
    [InlineData(Script.Bash, null, "4294967296", "one", "4096 MB")]
    [InlineData(Script.PowerShell, "max", "9223372036854771712", "lanes", "20480 MB")]
    [InlineData(Script.Bash, "max", "9223372036854771712", "lanes", "20480 MB")]
    [InlineData(Script.PowerShell, "68719476736", null, "lanes", "20480 MB")]
    [InlineData(Script.Bash, "68719476736", null, "lanes", "20480 MB")]
    public async Task The_gate_counts_the_smaller_of_the_free_memory_and_the_container_limit(Script script, string? cgroupV2, string? cgroupV1, string expectedMode, string expectedMemory)
    {
        var gate = await RunGateAsync(script, [], settings: FakeMemory(availableKilobytes: 20 * 1024 * 1024, cgroupV2, cgroupV1));

        Assert.SkipWhen(gate is null, "pwsh, bash or git is not installed here.");
        Assert.True(gate!.ExitCode == 0, gate.Output);
        if (expectedMode == "one")
        {
            AssertAppRanAsOneProcess(gate);
        }
        else
        {
            AssertAppRanInLanes(gate);
        }

        Assert.Contains(expectedMemory, gate.Output, StringComparison.Ordinal);
    }

    // 20 GB available in meminfo and a 16 GB limit in every case; what the container already uses decides.
    [Theory]
    [InlineData(Script.PowerShell, "17179869184", null, "8589934592", "one", "8192 MB")]
    [InlineData(Script.Bash, "17179869184", null, "8589934592", "one", "8192 MB")]
    [InlineData(Script.PowerShell, null, "17179869184", "8589934592", "one", "8192 MB")]
    [InlineData(Script.Bash, null, "17179869184", "8589934592", "one", "8192 MB")]
    [InlineData(Script.PowerShell, "17179869184", null, "1073741824", "lanes", "15360 MB")]
    [InlineData(Script.Bash, "17179869184", null, "1073741824", "lanes", "15360 MB")]
    public async Task The_gate_takes_what_the_container_already_uses_off_its_limit(Script script, string? cgroupV2, string? cgroupV1, string usage, string expectedMode, string expectedMemory)
    {
        var gate = await RunGateAsync(script, [], settings: FakeMemory(availableKilobytes: 20 * 1024 * 1024, cgroupV2, cgroupV1, usage));

        Assert.SkipWhen(gate is null, "pwsh, bash or git is not installed here.");
        Assert.True(gate!.ExitCode == 0, gate.Output);
        if (expectedMode == "one")
        {
            AssertAppRanAsOneProcess(gate);
        }
        else
        {
            AssertAppRanInLanes(gate);
        }

        Assert.Contains(expectedMemory, gate.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Script.PowerShell, "8", "4")]
    [InlineData(Script.Bash, "8", "4")]
    [InlineData(Script.PowerShell, "3", "3")]
    [InlineData(Script.Bash, "3", "3")]
    public async Task The_one_app_process_gets_up_to_four_threads_and_never_more_than_core(Script script, string testThreads, string expectedAppThreads)
    {
        var settings = Memory(2048);
        settings["SAVEPOINT_GATE_TEST_THREADS"] = testThreads;
        var gate = await RunGateAsync(script, [], settings: settings);

        Assert.SkipWhen(gate is null, "pwsh, bash or git is not installed here.");
        Assert.True(gate!.ExitCode == 0, gate.Output);
        var processes = TestProcesses(gate);
        Assert.Equal([testThreads], Values(processes.Single(process => process.Name == "core"), "--max-threads"));
        Assert.Equal([expectedAppThreads], Values(processes.Single(process => process.Name == "app"), "--max-threads"));
    }

    [Theory]
    [InlineData(Script.PowerShell)]
    [InlineData(Script.Bash)]
    public async Task A_lanes_setting_the_gate_does_not_know_is_refused_before_anything_is_built(Script script)
    {
        var settings = RoomToSpare();
        settings["SAVEPOINT_GATE_APP_LANES"] = "many";
        var gate = await RunGateAsync(script, [], settings: settings);

        Assert.SkipWhen(gate is null, "pwsh, bash or git is not installed here.");
        Assert.NotEqual(0, gate!.ExitCode);
        Assert.Contains("SAVEPOINT_GATE_APP_LANES", gate.Output, StringComparison.Ordinal);
        Assert.Empty(gate.Calls);
    }

    [Theory]
    [InlineData(Script.PowerShell)]
    [InlineData(Script.Bash)]
    public async Task The_count_check_still_fails_when_the_one_app_process_ran_fewer_tests_than_the_listing(Script script)
    {
        var gate = await RunGateAsync(script, [], listedMore: 1, settings: Memory(2048));

        Assert.SkipWhen(gate is null, "pwsh, bash or git is not installed here.");
        Assert.NotEqual(0, gate!.ExitCode);
        Assert.Contains("test count check failed", gate.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Script.PowerShell)]
    [InlineData(Script.Bash)]
    public async Task An_explicit_thread_count_goes_to_core_and_the_lanes_get_at_most_two(Script script)
    {
        var settings = RoomToSpare();
        settings["SAVEPOINT_GATE_TEST_THREADS"] = "8";
        var gate = await RunGateAsync(script, [], settings: settings);

        Assert.SkipWhen(gate is null, "pwsh, bash or git is not installed here.");
        Assert.True(gate!.ExitCode == 0, gate.Output);
        foreach (var process in TestProcesses(gate))
        {
            Assert.Equal([process.Name == "core" ? "8" : "2"], Values(process, "--max-threads"));
        }
    }

    [Theory]
    [InlineData(Script.PowerShell)]
    [InlineData(Script.Bash)]
    public async Task By_default_core_gets_at_least_four_threads_and_never_fewer_than_a_lane(Script script)
    {
        var gate = await RunGateAsync(script, []);

        Assert.SkipWhen(gate is null, "pwsh, bash or git is not installed here.");
        Assert.True(gate!.ExitCode == 0, gate.Output);
        var processes = TestProcesses(gate);
        var core = int.Parse(Values(processes.Single(process => process.Name == "core"), "--max-threads").Single(), CultureInfo.InvariantCulture);
        Assert.InRange(core, 4, 8);
        Assert.All(processes.Where(process => process.Name != "core"), process =>
            Assert.Equal(["2"], Values(process, "--max-threads")));
    }

    [Theory]
    [InlineData(Script.PowerShell)]
    [InlineData(Script.Bash)]
    public async Task Each_test_process_prints_its_output_under_its_own_name_and_a_slow_one_is_reported_while_it_runs(Script script)
    {
        var settings = RoomToSpare();
        settings["FAKE_SLOW"] = "core";
        settings["SAVEPOINT_GATE_HEARTBEAT_SECONDS"] = "1";
        var gate = await RunGateAsync(script, [], settings: settings);

        Assert.SkipWhen(gate is null, "pwsh, bash or git is not installed here.");
        Assert.True(gate!.ExitCode == 0, gate.Output);
        foreach (var name in LaneProcessNames())
        {
            Assert.Contains($"[{name}] output of {name}, first line", gate.Output, StringComparison.Ordinal);
            Assert.Contains($"[{name}] output of {name}, last line", gate.Output, StringComparison.Ordinal);
        }

        // Core sleeps for four seconds before its last line, so a report that it is still running comes before that line.
        var heartbeat = gate.Output.IndexOf("still running", StringComparison.Ordinal);
        Assert.True(heartbeat >= 0, "No report of a process still running:" + Environment.NewLine + gate.Output);
        Assert.Contains("core", gate.Output.Substring(heartbeat, Math.Min(120, gate.Output.Length - heartbeat)), StringComparison.Ordinal);
        Assert.True(heartbeat < gate.Output.IndexOf("[core] output of core, last line", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(Script.PowerShell)]
    [InlineData(Script.Bash)]
    public async Task The_listing_counts_two_methods_whose_names_differ_only_in_case_as_two(Script script)
    {
        var settings = RoomToSpare();
        settings["FAKE_CASE_VARIANT"] = "1";
        var gate = await RunGateAsync(script, [], listedMore: -1, settings: settings);

        Assert.SkipWhen(gate is null, "pwsh, bash or git is not installed here.");
        Assert.True(gate!.ExitCode == 0, gate.Output);
        Assert.Contains($"= {ListedTests()} test methods, as many as the full listing", gate.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_linux_gate_runs_every_test_process_with_its_temp_folder_on_disk_inside_the_tree_and_removes_it_after()
    {
        var gate = await RunGateAsync(Script.Bash, []);

        Assert.SkipWhen(gate is null, "pwsh, bash or git is not installed here.");
        Assert.True(gate!.ExitCode == 0, gate.Output);
        Assert.Equal(TestProcesses(gate).Count, gate.TempFolders.Count);
        Assert.All(gate.TempFolders, folder =>
        {
            var path = folder.Path.Replace('\\', '/');
            Assert.EndsWith("/artifacts/tmp", path, StringComparison.Ordinal);
            Assert.Contains(Path.GetFileName(_root), path, StringComparison.Ordinal);
            Assert.True(folder.Existed, $"{folder.Path} was not there when the test process started.");
        });
        Assert.False(Directory.Exists(Path.Combine(_root, "artifacts", "tmp")), "The gate left its temp folder behind.");
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
