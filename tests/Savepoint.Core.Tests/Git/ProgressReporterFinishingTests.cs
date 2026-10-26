using Savepoint.Core.Git;
using Savepoint.Core.Tests.Git.Engine;

namespace Savepoint.Core.Tests.Git;

// A switch that finishes a stopped one writes some files before git's own meter starts. git then counts only the files that are left, which
// can be fewer than the ones already written, and the bar must carry on from where the written files left it.
public sealed class ProgressReporterFinishingTests
{
    private const int _files = 10;
    private const long _each = 10;

    // Ten files of ten bytes, so the switch writes a hundred.
    private static CheckoutBytePlan Plan() =>
        new(
            [.. Enumerable.Range(0, _files).Select(i => new GitEngine.TreeChange('M', "old" + i, "new" + i, $"f{i}.dat"))],
            Enumerable.Range(0, _files).ToDictionary(i => "new" + i, _ => _each));

    [Fact]
    public void The_files_written_before_git_counts_are_reported_with_their_bytes()
    {
        var log = new ProgressLog();
        var reporter = new ProgressReporter(log, Plan());

        reporter.ObserveFinishing(3, 6, 30);
        reporter.Start();

        Assert.Equal(new FileProgress(3, 6, null, 30, 100), log.Last);
    }

    [Fact]
    public void Git_counts_that_are_smaller_than_the_files_already_written_are_added_to_them()
    {
        var log = new ProgressLog();
        var reporter = new ProgressReporter(log, Plan());
        reporter.ObserveFinishing(6, 6, 60);

        reporter.Observe(1, 4);
        reporter.Start();

        Assert.Equal(new FileProgress(7, 10, null, 70, 100), log.Last);
    }

    [Fact]
    public void The_last_count_and_the_last_byte_land_on_the_totals_and_nothing_went_back()
    {
        var log = new ProgressLog();
        var reporter = new ProgressReporter(log, Plan());
        reporter.ObserveFinishing(6, 6, 60);
        reporter.Observe(1, 4);
        reporter.Start();
        reporter.Observe(4, 4);

        reporter.Complete();

        Assert.Equal(new FileProgress(10, 10, null, 100, 100), log.Last);
        Assert.Equal(log.Reports.Select(report => report.FilesDone).Order(), log.Reports.Select(report => report.FilesDone));
        Assert.Equal(log.Reports.Select(report => report.BytesDone ?? 0).Order(), log.Reports.Select(report => report.BytesDone ?? 0));
        Assert.All(log.Reports, report => Assert.True(report.FilesTotal >= report.FilesDone));
    }

    [Fact]
    public void A_switch_with_no_finishing_step_counts_as_it_always_did()
    {
        var log = new ProgressLog();
        var reporter = new ProgressReporter(log, Plan());

        reporter.Observe(4, 10);
        reporter.Start();

        Assert.Equal(new FileProgress(4, 10, null, 40, 100), log.Last);
    }
}
