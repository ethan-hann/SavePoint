using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Git;
using Savepoint.Core.Services.Profiles;

namespace Savepoint.Core.Tests.Services;

// The install folder and the saves folder fill one bar: files and bytes add up, and a total is shown only once every folder has given its own.
public sealed class CaptureProgressCombinerTests
{
    private sealed class Log(List<string>? events = null) : IProgress<JobProgress>
    {
        public List<JobProgress> Reports { get; } = [];

        public void Report(JobProgress value)
        {
            Reports.Add(value);
            events?.Add("progress:" + value.CurrentPath);
        }
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

    [Fact]
    public void Every_name_of_both_folders_becomes_a_line_once_and_before_the_progress_that_shows_the_last_one()
    {
        var events = new List<string>();
        var combiner = new CaptureProgressCombiner(new Log(events), line => events.Add("line:" + line));
        var install = combiner.For(RepoRole.Install);
        var saves = combiner.For(RepoRole.External);

        install.Report(new FileProgress(3, 10, "c.bin", NewPaths: ["a.bin", "b.bin", "c.bin"]));
        saves.Report(new FileProgress(1, 2, "s.sav", NewPaths: ["s.sav"]));
        install.Report(new FileProgress(3, 10, "c.bin", 100, 9_000));

        Assert.Equal(["line:a.bin", "line:b.bin", "line:c.bin", "progress:c.bin", "line:s.sav", "progress:s.sav", "progress:c.bin"], events);
    }

    [Fact]
    public void A_large_file_is_announced_when_it_starts_and_reported_as_it_goes()
    {
        var lines = new List<JobLine>();
        var combiner = new CaptureProgressCombiner(new Log(), lines.Add);
        var install = combiner.For(RepoRole.Install);

        install.Report(new FileProgress(0, 3, null, 0, 9_000, Copying: new CopyDetail("Data/big.pak", 0, 5_000, Starting: true)));
        install.Report(new FileProgress(0, 3, null, 2_000, 9_000, Copying: new CopyDetail("Data/big.pak", 2_000, 5_000)));

        Assert.Equal(
            [
                JobLine.ForStep(JobSteps.CaptureFileStart, ("path", "Data/big.pak"), ("bytesTotal", 5_000L)),
                JobLine.ForStep(JobSteps.CaptureFileProgress, ("path", "Data/big.pak"), ("bytes", 2_000L), ("bytesTotal", 5_000L)),
            ],
            lines);
    }

    [Fact]
    public void A_folder_detail_names_the_folder_and_how_much_of_it_is_stored()
    {
        var lines = new List<JobLine>();
        var combiner = new CaptureProgressCombiner(new Log(), lines.Add);
        var saves = combiner.For(RepoRole.External);

        saves.Report(new FileProgress(0, 3, null, 400, 900, Copying: new CopyDetail(null, 400, 900)));

        Assert.Equal([JobLine.ForStep(JobSteps.CaptureFolderProgress, ("folder", RepoRole.External), ("bytes", 400L), ("bytesTotal", 900L))], lines);
    }

    [Fact]
    public void A_report_with_nothing_to_say_adds_no_line()
    {
        var lines = new List<JobLine>();
        var combiner = new CaptureProgressCombiner(new Log(), lines.Add);

        combiner.For(RepoRole.Install).Report(new FileProgress(1, 3, "a", 40, 100));

        Assert.Empty(lines);
    }
}
