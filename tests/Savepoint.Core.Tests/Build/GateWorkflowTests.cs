using System.Text.RegularExpressions;
using Savepoint.Testing;

namespace Savepoint.Core.Tests.Build;

// The gate workflow cannot run in a test, so these read it for the setup the Linux job must do before build/gate.sh starts: the tests
// that drive PowerShell scripts, gpg and a private D-Bus daemon skip when those tools are missing, and a skip nobody sees is a test
// that never ran.
public class GateWorkflowTests
{
    private const string _workflowPath = ".forgejo/workflows/gate.yml";

    private static string RepoRoot => TestEnvironment.RepoRoot ?? throw new InvalidOperationException("These tests read the repository's build files.");

    private static string[] LinuxSteps()
    {
        var workflow = File.ReadAllText(Path.Combine(RepoRoot, _workflowPath));
        var job = Regex.Match(workflow, @"(?ms)^  linux:\r?\n(.*?)(?=^  [a-z][a-z-]*:\r?\n|\z)").Groups[1].Value;
        Assert.NotEmpty(job);
        return Regex.Split(job[job.IndexOf("    steps:", StringComparison.Ordinal)..], @"\r?\n      - ").Skip(1).ToArray();
    }

    private static int IndexOfStep(string[] steps, string name)
    {
        var index = Array.FindIndex(steps, step => step.Contains($"name: {name}", StringComparison.Ordinal));
        Assert.True(index >= 0, $"The Linux job has no step named {name}.");
        return index;
    }

    [Fact]
    public void The_linux_job_installs_pwsh_as_a_tool_after_the_sdk_and_before_the_gate()
    {
        var steps = LinuxSteps();

        var sdk = IndexOfStep(steps, "Install the .NET SDK");
        var pwsh = IndexOfStep(steps, "Install PowerShell");
        var gate = IndexOfStep(steps, "Gate");

        Assert.True(sdk < pwsh && pwsh < gate, "PowerShell must be installed after the SDK that installs it and before the gate that needs it.");
        Assert.Matches(@"/opt/dotnet/dotnet tool install --tool-path /opt/pwsh PowerShell --version \d+\.\d+\.\d+", steps[pwsh]);
    }

    // The job runs as root and the gate as an ordinary user, so the tool folders must be readable by others.
    [Fact]
    public void The_gate_user_can_read_and_start_pwsh()
    {
        var steps = LinuxSteps();

        var install = steps[IndexOfStep(steps, "Install PowerShell")];
        var gate = steps[IndexOfStep(steps, "Gate")];

        Assert.Contains("chmod -R a+rX /opt/pwsh /opt/dotnet", install, StringComparison.Ordinal);
        Assert.Matches(@"PATH=""/opt/pwsh:/opt/dotnet:\$PATH""", gate);
        Assert.Contains("DOTNET_ROOT=/opt/dotnet", gate, StringComparison.Ordinal);
    }

    [Fact]
    public void The_linux_job_installs_gpg_and_a_message_bus_daemon_before_the_gate()
    {
        var steps = LinuxSteps();

        var install = IndexOfStep(steps, "Install the tools the tests drive");
        var gate = IndexOfStep(steps, "Gate");

        Assert.True(install < gate, "The tools must be installed before the gate runs.");
        Assert.Matches(@"apt-get install -y --no-install-recommends [^\r\n]*\bdbus\b", steps[install]);
        Assert.Matches(@"apt-get install -y --no-install-recommends [^\r\n]*\bgnupg\b", steps[install]);
    }

    // A test the job can now run must not stay on the list of skips the Linux gate expects.
    [Theory]
    [InlineData("Msys2SourcesScriptTests")]
    [InlineData("PackageSourceScriptTests")]
    [InlineData("ReleaseWorkflowTests")]
    [InlineData("WikiSyncTests")]
    [InlineData("DBusNotificationBusTests")]
    public void The_linux_list_of_expected_skips_does_not_name_tests_the_job_can_run(string testClass)
    {
        var lines = File.ReadAllLines(Path.Combine(RepoRoot, "build", "expected-skips", "linux-ci.txt"));

        var named = lines.Where(line => line.Contains($".{testClass}.", StringComparison.Ordinal)).ToArray();

        Assert.True(named.Length == 0, $"linux-ci.txt still names {named.Length} test(s) of {testClass}, such as {named.FirstOrDefault()}.");
    }
}
