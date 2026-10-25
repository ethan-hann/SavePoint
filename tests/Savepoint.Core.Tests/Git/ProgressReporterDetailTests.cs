using Savepoint.Core.Git;
using Savepoint.Core.Tests.Git.Engine;
using Savepoint.Core.Tests.Services;

namespace Savepoint.Core.Tests.Git;

// While git prints no names, the reports say what is being stored instead, rarely enough to read as a few lines and not as a stream.
public sealed class ProgressReporterDetailTests
{
    private static readonly string[] _bigNames = ["Data/a.pak", "Data/b.pak"];
    private static readonly long[] _bigSizes = [1_000, 3_000];

    private static (ProgressReporter Reporter, ProgressLog Log, ManualTimeProvider Clock) Started(long byteTotal = 10_000)
    {
        var log = new ProgressLog();
        var clock = new ManualTimeProvider();
        var reporter = new ProgressReporter(log, time: clock);
        reporter.SetTotal(5);
        reporter.SetByteTotal(byteTotal);
        reporter.Start();
        return (reporter, log, clock);
    }

    private static List<CopyDetail> Details(ProgressLog log) => [.. log.Reports.Where(report => report.Copying is not null).Select(report => report.Copying!.Value)];

    private static CopyDetail LatestDetail(ProgressLog log)
    {
        var details = Details(log);
        Assert.NotEmpty(details);
        return details[^1];
    }

    [Fact]
    public void The_first_big_file_is_announced_as_soon_as_its_batch_begins()
    {
        var (reporter, log, _) = Started();

        reporter.BeginBatch(_bigNames, _bigSizes, 0);

        Assert.Equal([new CopyDetail("Data/a.pak", 0, 1_000, Starting: true)], Details(log));
    }

    [Fact]
    public void A_batch_of_small_files_is_not_announced()
    {
        var (reporter, log, _) = Started();

        reporter.BeginBatch(null, null, 0);

        Assert.Empty(Details(log));
    }

    [Fact]
    public void How_far_the_big_files_are_comes_only_after_the_quiet_stretch()
    {
        var (reporter, log, clock) = Started();
        reporter.BeginBatch(_bigNames, _bigSizes, 0);

        clock.Advance(ProgressReporter.DetailInterval - TimeSpan.FromSeconds(1));
        reporter.ObserveWritten(500);
        Assert.Single(Details(log));

        clock.Advance(TimeSpan.FromSeconds(1));
        reporter.ObserveWritten(600);

        Assert.Equal(new CopyDetail("Data/a.pak", 600, 1_000), LatestDetail(log));
    }

    [Fact]
    public void The_file_git_is_on_is_found_by_adding_up_the_sizes_in_the_order_given()
    {
        var (reporter, log, clock) = Started();
        reporter.BeginBatch(_bigNames, _bigSizes, 0);

        clock.Advance(ProgressReporter.DetailInterval);
        reporter.ObserveWritten(2_500);

        Assert.Equal(new CopyDetail("Data/b.pak", 1_500, 3_000), LatestDetail(log));
    }

    [Fact]
    public void The_same_quiet_stretch_is_not_reported_again_on_every_tick()
    {
        var (reporter, log, clock) = Started();
        reporter.BeginBatch(_bigNames, _bigSizes, 0);
        clock.Advance(ProgressReporter.DetailInterval);
        reporter.ObserveWritten(1_000);

        reporter.ObserveWritten(1_100);
        reporter.ObserveWritten(1_200);
        clock.Advance(TimeSpan.FromSeconds(5));
        reporter.ObserveWritten(1_300);

        Assert.Equal(2, Details(log).Count);
    }

    [Fact]
    public void The_batch_counts_only_what_was_stored_since_it_began()
    {
        var (reporter, log, clock) = Started();
        reporter.ObserveWritten(3_000);
        reporter.BeginBatch(_bigNames, _bigSizes, 3_400);

        clock.Advance(ProgressReporter.DetailInterval);
        reporter.ObserveWritten(3_900);

        Assert.Equal(new CopyDetail("Data/a.pak", 500, 1_000), LatestDetail(log));
    }

    [Fact]
    public void A_batch_that_could_not_read_the_store_starts_from_the_last_figure_seen()
    {
        var (reporter, log, clock) = Started();
        reporter.ObserveWritten(3_000);
        reporter.BeginBatch(_bigNames, _bigSizes, null);

        clock.Advance(ProgressReporter.DetailInterval);
        reporter.ObserveWritten(3_900);

        Assert.Equal(new CopyDetail("Data/a.pak", 900, 1_000), LatestDetail(log));
    }

    [Fact]
    public void A_file_never_shows_as_done_before_its_name_arrives()
    {
        var (reporter, log, clock) = Started(byteTotal: 100_000);
        reporter.BeginBatch(_bigNames, _bigSizes, 0);

        clock.Advance(ProgressReporter.DetailInterval);
        reporter.ObserveWritten(9_000);

        var latest = LatestDetail(log);
        Assert.Equal("Data/b.pak", latest.Path);
        Assert.True(latest.BytesDone < latest.BytesTotal, "the file is shown as done while git is still storing it");
    }

    [Fact]
    public void A_quiet_stretch_in_a_batch_of_small_files_names_the_folder_figures()
    {
        var (reporter, log, clock) = Started();
        reporter.BeginBatch(null, null, 0);

        clock.Advance(ProgressReporter.DetailInterval);
        reporter.ObserveWritten(3_000);

        Assert.Equal([new CopyDetail(null, 3_000, 10_000)], Details(log));
    }

    [Fact]
    public void Names_that_arrive_count_as_something_said()
    {
        var (reporter, log, clock) = Started();
        reporter.BeginBatch(null, null, 0);
        clock.Advance(ProgressReporter.DetailInterval - TimeSpan.FromSeconds(5));
        reporter.FileHandled("Data/a.bin");
        reporter.EndBatch();

        clock.Advance(TimeSpan.FromSeconds(10));
        reporter.BeginBatch(null, null, 0);
        reporter.ObserveWritten(3_000);

        Assert.Empty(Details(log));
    }

    [Fact]
    public void Nothing_is_said_while_the_capture_is_only_finishing()
    {
        var (reporter, log, clock) = Started(byteTotal: 1_000);
        reporter.BeginBatch(null, null, 0);
        reporter.ObserveWritten(5_000);

        // The store has held still long enough to be finishing, and the quiet stretch is long enough for a detail, in the same look.
        clock.Advance(ProgressReporter.DetailInterval + TimeSpan.FromSeconds(5));
        reporter.ObserveWritten(5_000);

        Assert.True(log.Last.Finishing);
        Assert.Empty(Details(log));
    }

    [Fact]
    public void A_new_batch_ends_the_wait_for_the_last_one()
    {
        var (reporter, log, clock) = Started(byteTotal: 1_000);
        reporter.BeginBatch(null, null, 0);
        reporter.ObserveWritten(5_000);
        clock.Advance(ProgressReporter.FinishingWindow);
        reporter.ObserveWritten(5_000);
        Assert.True(log.Last.Finishing);

        reporter.EndBatch();
        reporter.BeginBatch(null, null, 5_000);

        Assert.False(log.Last.Finishing);
    }

    [Fact]
    public void The_end_of_a_batch_delivers_its_names_at_once_whatever_the_pace()
    {
        var (reporter, log, _) = Started();
        reporter.BeginBatch(null, null, 0);
        reporter.FileHandled("Data/a.bin");
        reporter.FileHandled("Data/b.bin");
        reporter.FileHandled("Data/c.bin");

        reporter.EndBatch();

        Assert.Equal(["Data/a.bin", "Data/b.bin", "Data/c.bin"], log.Reports.SelectMany(report => report.NewPaths ?? []));
        Assert.Contains("Data/c.bin", log.Last.NewPaths!);
    }

    [Fact]
    public void After_the_big_files_the_next_quiet_stretch_is_about_the_folder()
    {
        var (reporter, log, clock) = Started();
        reporter.BeginBatch(_bigNames, _bigSizes, 0);
        reporter.FileHandled("Data/a.pak");
        reporter.FileHandled("Data/b.pak");
        reporter.EndBatch();
        reporter.BeginBatch(null, null, 4_000);

        clock.Advance(ProgressReporter.DetailInterval);
        reporter.ObserveWritten(5_000);

        Assert.Null(LatestDetail(log).Path);
    }
}
