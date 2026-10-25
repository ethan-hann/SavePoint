using Savepoint.Core.Git;
using Savepoint.Core.Tests.Git.Engine;

namespace Savepoint.Core.Tests.Git;

// Every name git prints reaches a reader once, however fast git prints them and however the reports are paced.
public sealed class ProgressReporterNamesTests
{
    private static string[] Names(int count) => [.. Enumerable.Range(0, count).Select(i => $"Data/file{i:D3}.bin")];

    private static List<string> Delivered(ProgressLog log) => [.. log.Reports.SelectMany(report => report.NewPaths ?? [])];

    [Fact]
    public void A_burst_of_names_is_delivered_whole_and_in_order()
    {
        var log = new ProgressLog();
        var reporter = new ProgressReporter(log);
        reporter.SetTotal(222);
        reporter.Start();
        var names = Names(222);

        foreach (var name in names)
        {
            reporter.FileHandled(name);
        }

        reporter.Complete();

        Assert.Equal(names, Delivered(log));
    }

    [Fact]
    public void A_burst_is_still_throttled_into_a_few_reports()
    {
        var log = new ProgressLog();
        var reporter = new ProgressReporter(log);
        reporter.SetTotal(222);
        reporter.Start();

        foreach (var name in Names(222))
        {
            reporter.FileHandled(name);
        }

        reporter.Complete();

        Assert.True(log.Reports.Count < 50, $"{log.Reports.Count} reports for one burst is not throttled");
    }

    [Fact]
    public void A_name_is_not_repeated_by_the_byte_reports_that_follow_it()
    {
        var log = new ProgressLog();
        var reporter = new ProgressReporter(log);
        reporter.SetTotal(3);
        reporter.SetByteTotal(1000);
        reporter.Start();
        reporter.FileHandled("a.bin");

        reporter.ObserveWritten(300);
        reporter.ObserveWritten(600);
        reporter.FileHandled("b.bin");
        reporter.Complete();

        Assert.Equal(["a.bin", "b.bin"], Delivered(log));
    }

    [Fact]
    public void A_report_with_no_new_name_carries_none()
    {
        var log = new ProgressLog();
        var reporter = new ProgressReporter(log);
        reporter.SetByteTotal(1000);
        reporter.Start();

        reporter.ObserveWritten(400);

        Assert.All(log.Reports, report => Assert.Null(report.NewPaths));
    }

    [Fact]
    public void Files_an_earlier_attempt_already_staged_count_as_done_from_the_start()
    {
        var log = new ProgressLog();
        var reporter = new ProgressReporter(log);
        reporter.SetTotal(10);
        reporter.SetAlreadyDone(6);
        reporter.Start();

        foreach (var name in Names(4))
        {
            reporter.FileHandled(name);
        }

        reporter.Complete();

        Assert.Equal(6, log.Reports[0].FilesDone);
        Assert.Equal(10, log.Last.FilesDone);
        Assert.Equal(10, log.Last.FilesTotal);
        Assert.Equal(4, Delivered(log).Count);
    }

    [Fact]
    public void A_total_that_shrank_after_the_last_report_still_reaches_the_closing_report()
    {
        var log = new ProgressLog();
        var reporter = new ProgressReporter(log);
        reporter.SetTotal(10);
        reporter.Start();
        foreach (var name in Names(9))
        {
            reporter.FileHandled(name);
        }

        reporter.EndBatch();

        // The tenth file was gone before its group, so the capture expects nine.
        reporter.SetTotal(9);
        reporter.Complete();

        Assert.Equal(9, log.Last.FilesDone);
        Assert.Equal(9, log.Last.FilesTotal);
    }

    [Fact]
    public void Names_not_yet_reported_when_the_capture_fails_are_still_delivered()
    {
        var log = new ProgressLog();
        var reporter = new ProgressReporter(log);
        reporter.SetTotal(3);
        reporter.Start();
        reporter.FileHandled("a.bin");
        reporter.FileHandled("b.bin");
        reporter.FileHandled("c.bin");

        reporter.Abandon();

        Assert.Equal(["a.bin", "b.bin", "c.bin"], Delivered(log));
    }

    [Fact]
    public void Names_of_a_group_git_did_not_finish_are_never_delivered_when_the_capture_stops()
    {
        var log = new ProgressLog();
        var reporter = new ProgressReporter(log);
        reporter.SetTotal(6);
        reporter.Start();
        reporter.BeginBatch(null, null, 0);
        reporter.FileHandled("a.bin");
        reporter.FileHandled("b.bin");
        reporter.FileHandled("c.bin");

        reporter.Abandon();

        Assert.Empty(Delivered(log));
        Assert.Equal(0, log.Last.FilesDone);
    }

    [Fact]
    public void A_group_that_ended_keeps_its_names_when_the_next_one_is_stopped()
    {
        var log = new ProgressLog();
        var reporter = new ProgressReporter(log);
        reporter.SetTotal(6);
        reporter.Start();
        reporter.BeginBatch(null, null, 0);
        reporter.FileHandled("a.bin");
        reporter.FileHandled("b.bin");
        reporter.EndBatch();
        reporter.BeginBatch(null, null, 0);
        reporter.FileHandled("c.bin");

        reporter.Abandon();

        Assert.Equal(["a.bin", "b.bin"], Delivered(log));
        Assert.Equal(2, log.Last.FilesDone);
    }

    [Fact]
    public void A_removal_made_before_the_first_group_is_kept_when_that_group_is_stopped()
    {
        var log = new ProgressLog();
        var reporter = new ProgressReporter(log);
        reporter.SetTotal(5);
        reporter.Start();
        reporter.FileHandled("gone1.bin");
        reporter.FileHandled("gone2.bin");
        reporter.BeginBatch(null, null, 0);
        reporter.FileHandled("a.bin");

        reporter.Abandon();

        Assert.Equal(["gone1.bin", "gone2.bin"], Delivered(log));
        Assert.Equal(2, log.Last.FilesDone);
    }

    [Fact]
    public void A_groups_names_are_delivered_once_its_add_ends_and_count_as_done_then()
    {
        var log = new ProgressLog();
        var reporter = new ProgressReporter(log);
        reporter.SetTotal(3);
        reporter.Start();
        reporter.BeginBatch(null, null, 0);
        reporter.FileHandled("a.bin");
        reporter.FileHandled("b.bin");
        reporter.FileHandled("c.bin");
        Assert.Empty(Delivered(log));

        reporter.EndBatch();

        Assert.Equal(["a.bin", "b.bin", "c.bin"], Delivered(log));
        Assert.Equal(3, log.Last.FilesDone);
    }
}
