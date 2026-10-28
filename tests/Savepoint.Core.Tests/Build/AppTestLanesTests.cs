using System.Globalization;
using System.Text.RegularExpressions;
using Savepoint.Testing;

namespace Savepoint.Core.Tests.Build;

// The gates run the App tests in several processes, each with a namespace filter from build/app-test-lanes.txt, and compare the sum of
// their counts with a full listing. These tests read the lane file against the test sources, and run the count check on made-up reports.
public sealed partial class AppTestLanesTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("savepoint-lanes-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static string RepoRoot => TestEnvironment.RepoRoot ?? throw new InvalidOperationException("These tests read the repository's build files.");

    private static string AppTestsFolder => Path.Combine(RepoRoot, "tests", "Savepoint.App.Tests");

    private sealed record Lane(string Name, string Namespace);

    private static List<Lane> ReadLanes()
    {
        var lanes = new List<Lane>();
        foreach (var line in File.ReadAllLines(Path.Combine(RepoRoot, "build", "app-test-lanes.txt")).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')))
        {
            var words = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            Assert.True(words.Length == 2, $"A lane line is '<name> <namespace>', got '{line}'.");
            lanes.Add(new Lane(words[0], words[1]));
        }

        return lanes;
    }

    [GeneratedRegex(@"^namespace\s+([A-Za-z0-9_.]+)\s*[;{]?\s*$", RegexOptions.Multiline)]
    private static partial Regex NamespaceDeclaration();

    // The namespace of each source file under the App tests, with whether the file holds tests.
    private static List<(string Namespace, bool HasTests)> SourceNamespaces()
    {
        var files = Directory.EnumerateFiles(AppTestsFolder, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
        var result = new List<(string, bool)>();
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            var match = NamespaceDeclaration().Match(text);
            if (match.Success)
            {
                result.Add((match.Groups[1].Value, TestAttribute().IsMatch(text)));
            }
        }

        return result;
    }

    [GeneratedRegex(@"\[(Avalonia)?(Fact|Theory)\b")]
    private static partial Regex TestAttribute();

    // The lanes' own filters match a namespace exactly (--filter-namespace X); "rest" runs what no lane's filter names.
    private static List<string> ProcessesRunning(string testNamespace, IReadOnlyList<Lane> lanes)
    {
        var processes = lanes.Where(lane => lane.Namespace == testNamespace).Select(lane => lane.Name).ToList();
        if (!lanes.Any(lane => lane.Namespace == testNamespace))
        {
            processes.Add("rest");
        }

        return processes;
    }

    [Fact]
    public void Every_namespace_of_the_app_tests_runs_in_exactly_one_process()
    {
        var lanes = ReadLanes();
        var namespaces = SourceNamespaces().Select(entry => entry.Namespace).Distinct().Order(StringComparer.Ordinal).ToList();
        Assert.NotEmpty(namespaces);

        var wrong = namespaces.Where(ns => ProcessesRunning(ns, lanes).Count != 1).ToList();

        Assert.True(wrong.Count == 0, "These namespaces do not run in exactly one process: " + string.Join(", ", wrong));
    }

    [Fact]
    public void Every_lane_names_a_namespace_that_holds_tests_and_no_two_lanes_share_one()
    {
        var lanes = ReadLanes();
        var withTests = SourceNamespaces().Where(entry => entry.HasTests).Select(entry => entry.Namespace).ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(lanes);
        Assert.Distinct(lanes.Select(lane => lane.Name), StringComparer.Ordinal);
        Assert.Distinct(lanes.Select(lane => lane.Namespace), StringComparer.Ordinal);
        Assert.DoesNotContain("rest", lanes.Select(lane => lane.Name));
        var missing = lanes.Where(lane => !withTests.Contains(lane.Namespace)).Select(lane => lane.Namespace).ToList();
        Assert.True(missing.Count == 0, "No test of the App tests is in: " + string.Join(", ", missing));
    }

    // A report of a process that ran this many test methods. The first method is a theory with three cases, so a count of cases would
    // not match a count of methods.
    private string Report(string name, int methods)
    {
        var tests = new System.Text.StringBuilder();
        for (var method = 0; method < methods; method++)
        {
            var cases = method == 0 ? 3 : 1;
            for (var row = 0; row < cases; row++)
            {
                tests.Append(CultureInfo.InvariantCulture, $"<test id=\"{method}-{row}\" name=\"Lane{name}.Tests.Method{method}(row: {row})\" result=\"Pass\" type=\"Lane{name}.Tests\" method=\"Method{method}\" />");
            }
        }

        var path = Path.Combine(_root, name);
        File.WriteAllText(path, $"<?xml version=\"1.0\" encoding=\"utf-8\"?><assemblies schema-version=\"3\"><assembly id=\"a\" name=\"Savepoint.App.Tests.dll\"><collection id=\"c\">{tests}</collection></assembly></assemblies>");
        return path;
    }

    public enum Script
    {
        PowerShell,
        Bash,
    }

    // Runs a count check script on report paths; null when its shell is not installed here.
    private async Task<ScriptProcess.Result?> RunCountCheckAsync(Script script, int expected, params string[] reports)
    {
        if (script == Script.PowerShell)
        {
            var arguments = new List<string> { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(RepoRoot, "build", "check-test-counts.ps1"), "-Expected", expected.ToString(System.Globalization.CultureInfo.InvariantCulture) };
            arguments.AddRange(reports);
            return await ScriptProcess.RunAsync("pwsh", arguments, _root);
        }

        var bashArguments = new List<string> { ScriptProcess.ForBash(Path.Combine(RepoRoot, "build", "check-test-counts.sh")), expected.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        bashArguments.AddRange(reports.Select(ScriptProcess.ForBash));
        return await ScriptProcess.RunAsync(ScriptProcess.BashProgram(), bashArguments, _root);
    }

    [Theory]
    [InlineData(Script.PowerShell)]
    [InlineData(Script.Bash)]
    public async Task The_count_check_passes_when_the_processes_add_up_to_the_full_listing(Script script)
    {
        var run = await RunCountCheckAsync(script, 11, Report("a.xunit", 5), Report("b.xunit", 2), Report("c.xunit", 4));

        Assert.SkipWhen(run is null, script + " is not installed here.");
        Assert.True(run!.ExitCode == 0, run.Output);
        Assert.Contains("5 + 2 + 4 = 11", run.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Script.PowerShell, 10)]
    [InlineData(Script.PowerShell, 12)]
    [InlineData(Script.Bash, 10)]
    [InlineData(Script.Bash, 12)]
    public async Task The_count_check_fails_when_the_processes_ran_more_or_fewer_tests_than_the_listing_holds(Script script, int listed)
    {
        var run = await RunCountCheckAsync(script, listed, Report("a.xunit", 5), Report("b.xunit", 2), Report("c.xunit", 4));

        Assert.SkipWhen(run is null, script + " is not installed here.");
        Assert.Equal(1, run!.ExitCode);
        Assert.Contains("= 11 test methods", run.Output, StringComparison.Ordinal);
        Assert.Contains(listed.ToString(System.Globalization.CultureInfo.InvariantCulture), run.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Script.PowerShell)]
    [InlineData(Script.Bash)]
    public async Task The_count_check_fails_when_a_process_wrote_no_report(Script script)
    {
        var run = await RunCountCheckAsync(script, 11, Report("a.xunit", 5), Report("b.xunit", 2), Path.Combine(_root, "missing.xunit"));

        Assert.SkipWhen(run is null, script + " is not installed here.");
        Assert.Equal(1, run!.ExitCode);
        Assert.Contains("missing.xunit", run.Output, StringComparison.Ordinal);
    }
}
