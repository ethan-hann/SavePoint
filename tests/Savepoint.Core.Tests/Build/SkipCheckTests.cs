using System.Text;
using Savepoint.Tools.SkipCheck;

namespace Savepoint.Core.Tests.Build;

// The gate compares the tests a run skipped with a checked-in list, so a probe that breaks cannot quietly turn real tests into skips.
public sealed class SkipCheckTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("savepoint-skipcheck-");

    public void Dispose() => _folder.Delete(recursive: true);

    private static string Report(params (string Name, string Result, string? Reason)[] tests)
    {
        var xml = new StringBuilder("<assemblies><assembly><collection>");
        foreach (var (name, result, reason) in tests)
        {
            xml.Append("<test name=\"").Append(name).Append("\" result=\"").Append(result).Append('"');
            xml.Append(reason is null ? " />" : $"><reason><![CDATA[{reason}]]></reason></test>");
        }

        return xml.Append("</collection></assembly></assemblies>").ToString();
    }

    private static XunitReport ReadReport(params string[] reports) => XunitReport.Read(reports.Select(r => (TextReader)new StringReader(r)));

    private string WriteFile(string relative, string content)
    {
        var path = Path.Combine(_folder.FullName, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static SkipList ParseList(params string[] lines) => SkipList.Parse(lines);

    [Fact]
    public void A_list_holds_its_names_and_ignores_blank_lines_and_comments()
    {
        var list = ParseList("# a comment", "", "  A.B.One  ", "A.B.Two(x: 1)");

        Assert.Equal(["A.B.One", "A.B.Two(x: 1)"], list.Expected.Order());
        Assert.Empty(list.Intermittent);
    }

    [Fact]
    public void A_name_after_a_tilde_may_skip_without_being_required_to()
    {
        var list = ParseList("A.B.One", "~A.B.Timing");

        Assert.Equal(["A.B.One"], list.Expected);
        Assert.Equal(["A.B.Timing"], list.Intermittent);
    }

    [Fact]
    public void A_list_file_is_read_from_disk()
    {
        var path = WriteFile("lists/own.txt", "A.B.Own\n~A.B.Timing\n");

        var list = SkipList.Load(path);

        Assert.Equal(["A.B.Own"], list.Expected);
        Assert.Equal(["A.B.Timing"], list.Intermittent);
    }

    [Fact]
    public void A_report_names_each_skipped_test_with_its_reason_and_each_test_that_ran()
    {
        var report = ReadReport(Report(
            ("N.C.Passes", "Pass", null),
            ("N.C.Fails", "Fail", null),
            ("N.C.Skips", "Skip", "no folder links"),
            ("N.C.Theory(kind: Unreadable)", "Skip", "no permissions")));

        Assert.Equal("no folder links", report.Skipped["N.C.Skips"]);
        Assert.Equal("no permissions", report.Skipped["N.C.Theory(kind: Unreadable)"]);
        Assert.Equal(2, report.Skipped.Count);
        Assert.Equal(["N.C.Fails", "N.C.Passes"], report.Ran.Order());
    }

    [Fact]
    public void Reports_of_several_test_projects_are_merged_and_a_test_that_ran_once_counts_as_run()
    {
        var report = ReadReport(
            Report(("N.C.One", "Skip", "x"), ("N.C.Two", "Skip", "y")),
            Report(("N.C.One", "Pass", null), ("N.C.Three", "Pass", null)));

        Assert.Equal(["N.C.Two"], report.Skipped.Keys);
        Assert.Equal(["N.C.One", "N.C.Three"], report.Ran.Order());
    }

    [Fact]
    public void A_run_whose_skips_are_all_listed_passes()
    {
        var verdict = SkipVerdict.Compare(ParseList("N.C.Skips"), ReadReport(Report(("N.C.Skips", "Skip", "r"), ("N.C.Passes", "Pass", null))));

        Assert.True(verdict.Passed);
        Assert.Empty(verdict.Unexpected);
        Assert.Empty(verdict.NowRunning);
        Assert.Equal(2, verdict.TotalTests);
    }

    [Fact]
    public void A_skip_the_list_does_not_name_fails_and_is_named()
    {
        var verdict = SkipVerdict.Compare(
            ParseList("N.C.Skips"),
            ReadReport(Report(("N.C.Skips", "Skip", "r"), ("N.C.Zebra", "Skip", "r"), ("N.C.Apple", "Skip", "r"))));

        Assert.False(verdict.Passed);
        Assert.Equal(["N.C.Apple", "N.C.Zebra"], verdict.Unexpected);
    }

    [Fact]
    public void A_listed_test_that_ran_is_a_note_and_one_missing_from_the_report_is_only_counted()
    {
        var verdict = SkipVerdict.Compare(
            ParseList("N.C.Ran", "N.C.Skips", "N.C.Gone"),
            ReadReport(Report(("N.C.Ran", "Pass", null), ("N.C.Skips", "Skip", "r"))));

        Assert.True(verdict.Passed);
        Assert.Equal(["N.C.Ran"], verdict.NowRunning);
        Assert.Equal(1, verdict.NotInReport);
    }

    [Fact]
    public void A_test_that_may_skip_never_fails_the_run_and_is_never_a_note()
    {
        var skipped = SkipVerdict.Compare(ParseList("~N.C.Timing"), ReadReport(Report(("N.C.Timing", "Skip", "r"))));
        var ran = SkipVerdict.Compare(ParseList("~N.C.Timing"), ReadReport(Report(("N.C.Timing", "Pass", null))));

        Assert.True(skipped.Passed);
        Assert.Empty(skipped.NowRunning);
        Assert.True(ran.Passed);
        Assert.Empty(ran.NowRunning);
    }

    private (int Code, string Output, string Error) RunProgram(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var code = SkipCheckProgram.Run(args, output, error);
        return (code, output.ToString(), error.ToString());
    }

    private string ReportsFolder(params string[] reports)
    {
        var folder = Path.Combine(_folder.FullName, "reports-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        for (var i = 0; i < reports.Length; i++)
        {
            // The test platform names its xunit report with the .xunit extension unless a file name is given.
            File.WriteAllText(Path.Combine(folder, $"module{i}.xunit"), reports[i]);
        }

        return folder;
    }

    [Fact]
    public void The_check_exits_with_1_and_prints_every_unexpected_skip_on_a_line_of_its_own()
    {
        var reports = ReportsFolder(Report(("N.C.Skips", "Skip", "r"), ("N.C.Broken", "Skip", "this system cannot make folder links"), ("N.C.Other", "Skip", "this system cannot make folder links")));
        var list = WriteFile("expected.txt", "N.C.Skips\n");

        var (code, _, error) = RunProgram("--reports", reports, "--expected", list);

        Assert.Equal(1, code);
        var lines = error.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("N.C.Broken", lines);
        Assert.Contains("N.C.Other", lines);
        Assert.DoesNotContain("N.C.Skips", lines);
        Assert.Contains("2 x this system cannot make folder links", error);
    }

    [Fact]
    public void The_check_exits_with_0_and_says_which_listed_tests_ran()
    {
        var reports = ReportsFolder(Report(("N.C.Ran", "Pass", null), ("N.C.Skips", "Skip", "r")));
        var list = WriteFile("expected.txt", "N.C.Ran\nN.C.Skips\n");

        var (code, output, _) = RunProgram("--reports", reports, "--expected", list);

        Assert.Equal(0, code);
        Assert.Contains("N.C.Ran", output);
        Assert.DoesNotContain("N.C.Skips", output);
    }

    [Fact]
    public void The_check_reads_reports_in_folders_below_the_one_it_is_given()
    {
        var reports = ReportsFolder();
        WriteFile(Path.Combine(Path.GetFileName(reports), "retry", "again.xml"), Report(("N.C.Broken", "Skip", "r")));
        var list = WriteFile("expected.txt", "# nothing\n");

        Assert.Equal(1, RunProgram("--reports", reports, "--expected", list).Code);
    }

    [Fact]
    public void The_check_exits_with_2_when_there_is_no_report()
    {
        var (code, _, error) = RunProgram("--reports", ReportsFolder(), "--expected", WriteFile("expected.txt", ""));

        Assert.Equal(2, code);
        Assert.Contains("no xunit report", error);
    }

    [Fact]
    public void The_check_exits_with_2_when_the_reports_hold_no_tests()
    {
        var reports = ReportsFolder(Report());

        var (code, _, error) = RunProgram("--reports", reports, "--expected", WriteFile("expected.txt", ""));

        Assert.Equal(2, code);
        Assert.Contains("no tests", error);
    }

    [Fact]
    public void The_check_exits_with_2_when_the_list_is_missing_or_an_argument_is_unknown()
    {
        var reports = ReportsFolder(Report(("N.C.Passes", "Pass", null)));

        Assert.Equal(2, RunProgram("--reports", reports, "--expected", Path.Combine(_folder.FullName, "none.txt")).Code);
        Assert.Equal(2, RunProgram("--reports", reports).Code);
        Assert.Equal(2, RunProgram("--reports", reports, "--expected", WriteFile("e.txt", ""), "--bogus").Code);
    }

    [Theory]
    [InlineData("windows")]
    [InlineData("linux-ci")]
    public void The_checked_in_lists_hold_full_test_names_once_each(string name)
    {
        var root = Testing.TestEnvironment.RepoRoot;
        if (root is null)
        {
            return; // Binaries run outside a checkout: there is no list to read.
        }

        var path = Path.Combine(root, "build", "expected-skips", name + ".txt");
        var names = File.ReadAllLines(path).Select(line => line.Trim().TrimStart('~')).Where(line => line.Length > 0 && line[0] != '#').ToList();
        var list = SkipList.Load(path);

        Assert.All(names, entry => Assert.StartsWith("Savepoint.", entry));
        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.Empty(list.Expected.Intersect(list.Intermittent));
    }

    [Fact]
    public void Write_rewrites_the_list_from_the_run_keeping_its_header_and_timing_entries()
    {
        var path = WriteFile("derived.txt", "# header line\n# second header line\n\nN.C.Stale\n~N.C.Timing\n");
        var reports = ReportsFolder(Report(("N.C.Zed", "Skip", "r"), ("N.C.Apple", "Skip", "r"), ("N.C.Timing", "Skip", "r")));

        var (code, _, _) = RunProgram("--reports", reports, "--expected", path, "--write");

        Assert.Equal(0, code);
        Assert.Equal(
            ["# header line", "# second header line", "", "N.C.Apple", "N.C.Zed", "~N.C.Timing"],
            File.ReadAllLines(path));
    }
}
