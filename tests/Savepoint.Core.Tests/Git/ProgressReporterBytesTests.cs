using Savepoint.Core.Git;
using Savepoint.Core.Tests.Git.Engine;

namespace Savepoint.Core.Tests.Git;

// The byte side of a capture's progress reports: known before git writes anything, moving while one file is stored, never wrong.
public sealed class ProgressReporterBytesTests
{
    [Fact]
    public void Starting_reports_the_totals_before_any_file_is_handled()
    {
        var log = new ProgressLog();
        var reporter = new ProgressReporter(log);
        reporter.SetTotal(5);
        reporter.SetByteTotal(1000);

        reporter.Start();

        Assert.Equal([new FileProgress(0, 5, null, 0, 1000)], log.Reports);
    }

    [Fact]
    public void Bytes_written_move_the_report_while_no_file_has_finished()
    {
        var log = new ProgressLog();
        var reporter = new ProgressReporter(log);
        reporter.SetTotal(5);
        reporter.SetByteTotal(1000);
        reporter.Start();

        reporter.ObserveWritten(400);
        reporter.ObserveWritten(650);

        Assert.Equal([0L, 400L, 650L], log.Reports.Select(report => report.BytesDone!.Value));
        Assert.All(log.Reports, report => Assert.Equal(0, report.FilesDone));
    }

    [Fact]
    public void Bytes_written_never_go_backwards()
    {
        var log = new ProgressLog();
        var reporter = new ProgressReporter(log);
        reporter.SetByteTotal(1000);
        reporter.Start();
        reporter.ObserveWritten(400);

        reporter.ObserveWritten(300);

        Assert.Equal([0L, 400L], log.Reports.Select(report => report.BytesDone!.Value));
    }

    [Fact]
    public void Bytes_written_stay_short_of_the_total_until_the_capture_completes()
    {
        var log = new ProgressLog();
        var reporter = new ProgressReporter(log);
        reporter.SetByteTotal(1000);
        reporter.Start();

        reporter.ObserveWritten(5000);
        var beforeCompletion = log.Last.BytesDone;
        reporter.Complete();

        Assert.True(beforeCompletion < 1000, $"{beforeCompletion} of 1000 was shown before the capture was done");
        Assert.Equal(1000, log.Last.BytesDone);
        Assert.Equal(1000, log.Last.BytesTotal);
    }

    [Fact]
    public void A_byte_report_carries_the_files_and_name_reported_so_far()
    {
        var log = new ProgressLog();
        var reporter = new ProgressReporter(log);
        reporter.SetTotal(4);
        reporter.SetByteTotal(1000);
        reporter.Start();
        reporter.FileHandled("Data/a.bin");

        reporter.ObserveWritten(500);

        Assert.Equal(new FileProgress(1, 4, "Data/a.bin", 500, 1000), log.Last);
    }

    [Fact]
    public void Completing_delivers_every_byte_even_when_no_file_line_arrived()
    {
        var log = new ProgressLog();
        var reporter = new ProgressReporter(log);
        reporter.SetTotal(2);
        reporter.SetByteTotal(1000);
        reporter.Start();

        reporter.Complete();

        Assert.Equal(new FileProgress(0, 2, null, 1000, 1000), log.Last);
    }

    [Fact]
    public void Reports_carry_no_bytes_when_no_byte_total_was_set()
    {
        var log = new ProgressLog();
        var reporter = new ProgressReporter(log);
        reporter.SetTotal(2);

        reporter.FileHandled("a.txt");

        Assert.Equal(new FileProgress(1, 2, "a.txt"), log.Last);
    }
}
