using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Git;
using Savepoint.Core.Services.Profiles;

namespace Savepoint.Core.Tests.Services;

// The install folder and the saves folder fill one bar: files and bytes add up, and a total is shown only once every folder has given its own.
public sealed class CaptureProgressCombinerTests
{
    private sealed class Log : IProgress<JobProgress>
    {
        public List<JobProgress> Reports { get; } = [];

        public void Report(JobProgress value) => Reports.Add(value);
    }

    [Fact]
    public void Bytes_and_files_of_both_folders_are_added_together()
    {
        var log = new Log();
        var combiner = new CaptureProgressCombiner(log);
        var install = combiner.For(RepoRole.Install);
        var saves = combiner.For(RepoRole.External);

        install.Report(new FileProgress(2, 10, "a", 300, 9_000));
        saves.Report(new FileProgress(1, 3, "b", 40, 100));

        Assert.Equal(new JobProgress(3, 13, 340, 9_100, "b"), log.Reports[^1]);
    }

    [Fact]
    public void No_total_is_shown_until_every_folder_has_reported_its_own()
    {
        var log = new Log();
        var combiner = new CaptureProgressCombiner(log);
        var install = combiner.For(RepoRole.Install);
        combiner.For(RepoRole.External);

        install.Report(new FileProgress(0, 10, null, 0, 9_000));

        Assert.Null(log.Reports[^1].FilesTotal);
        Assert.Null(log.Reports[^1].BytesTotal);
        Assert.Equal(0, log.Reports[^1].BytesDone);
    }

    [Fact]
    public void A_folder_that_had_nothing_to_store_stops_holding_the_total_back()
    {
        var log = new Log();
        var combiner = new CaptureProgressCombiner(log);
        var install = combiner.For(RepoRole.Install);
        var saves = combiner.For(RepoRole.External);
        install.Report(new FileProgress(0, 10, null, 0, 9_000));

        saves.Finish();

        Assert.Equal(new JobProgress(0, 10, 0, 9_000), log.Reports[^1]);
    }

    [Fact]
    public void A_folder_that_is_finished_counts_all_its_bytes_as_done()
    {
        var log = new Log();
        var combiner = new CaptureProgressCombiner(log);
        var install = combiner.For(RepoRole.Install);
        install.Report(new FileProgress(1, 2, "a", 500, 9_000));

        install.Finish();

        Assert.Equal(new JobProgress(2, 2, 9_000, 9_000), log.Reports[^1] with { CurrentPath = null });
    }

    [Fact]
    public void A_single_folder_that_is_finishing_makes_the_job_finishing()
    {
        var log = new Log();
        var combiner = new CaptureProgressCombiner(log);
        var install = combiner.For(RepoRole.Install);

        install.Report(new FileProgress(4, 10, "a", 8_900, 9_000, Finishing: true));

        Assert.True(log.Reports[^1].Finishing);
    }

    [Fact]
    public void The_job_is_not_finishing_while_the_other_folder_is_still_storing()
    {
        var log = new Log();
        var combiner = new CaptureProgressCombiner(log);
        var install = combiner.For(RepoRole.Install);
        var saves = combiner.For(RepoRole.External);
        saves.Report(new FileProgress(1, 3, "b", 40, 100));

        install.Report(new FileProgress(4, 10, "a", 8_900, 9_000, Finishing: true));

        Assert.False(log.Reports[^1].Finishing);
    }

    [Fact]
    public void The_job_is_finishing_once_the_other_folder_has_stored_everything()
    {
        var log = new Log();
        var combiner = new CaptureProgressCombiner(log);
        var install = combiner.For(RepoRole.Install);
        var saves = combiner.For(RepoRole.External);
        saves.Report(new FileProgress(3, 3, "b", 100, 100));

        install.Report(new FileProgress(4, 10, "a", 8_900, 9_000, Finishing: true));

        Assert.True(log.Reports[^1].Finishing);
    }

    [Fact]
    public void A_finished_folder_is_no_longer_finishing()
    {
        var log = new Log();
        var combiner = new CaptureProgressCombiner(log);
        var install = combiner.For(RepoRole.Install);
        install.Report(new FileProgress(4, 10, "a", 8_900, 9_000, Finishing: true));

        install.Finish();

        Assert.False(log.Reports[^1].Finishing);
    }
}
