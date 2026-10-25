using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

[Collection(EngineCollections.D)]
public sealed class CaptureProgressTests : IDisposable
{
    private readonly EngineWorkspace _ws = new();

    public void Dispose() => _ws.Dispose();

    [Fact]
    public async Task A_first_capture_expects_exactly_the_files_it_will_record_and_ends_complete()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.WriteMany(repo, "Data", 40);
        _ws.WriteMany(repo, "Logs", 15, "log");
        _ws.Write(repo, "game.exe", "exe");
        var progress = new ProgressLog();

        await _ws.CaptureAsync(repo, "profile-1", ignore: ["Logs/"], progress: progress);

        var last = progress.Last;
        Assert.Equal(41, last.FilesDone);
        Assert.Equal(41, last.FilesTotal);
        Assert.Equal(41, (await _ws.TreeAsync(repo, "profile-1")).Length);
    }

    [Fact]
    public async Task Reports_never_show_more_done_than_expected_and_never_go_backwards()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.WriteMany(repo, "Data", 120);
        var progress = new ProgressLog();

        await _ws.CaptureAsync(repo, "profile-1", progress: progress);

        var reports = progress.Reports;
        Assert.NotEmpty(reports);
        Assert.All(reports, report => Assert.True(report.FilesTotal is null || report.FilesDone <= report.FilesTotal));
        Assert.Equal(reports.OrderBy(report => report.FilesDone).Select(report => report.FilesDone), reports.Select(report => report.FilesDone));
    }

    [Fact]
    public async Task A_later_capture_counts_only_the_files_that_changed()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.WriteMany(repo, "Data", 60);
        await _ws.CaptureAsync(repo, "profile-1");
        _ws.Write(repo, "new.txt", "new");
        var progress = new ProgressLog();

        await _ws.CaptureAsync(repo, "profile-1", progress: progress);

        Assert.Equal(new FileProgress(0, 1, null, 0, 3), progress.Reports[0]);
        Assert.Equal(new FileProgress(1, 1, "new.txt", 3, 3), progress.Last);
    }

    [Fact]
    public async Task Files_rewritten_with_the_same_bytes_are_not_counted()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "slot1.sav", "save one");
        _ws.Write(repo, "slot2.sav", "save two");
        await _ws.CaptureAsync(repo, "profile-1");
        foreach (var (name, content) in new[] { ("slot1.sav", "save one"), ("slot2.sav", "save two") })
        {
            File.SetLastWriteTimeUtc(_ws.Write(repo, name, content), DateTime.UtcNow.AddMinutes(30));
        }

        var progress = new ProgressLog();
        var outcome = await _ws.CaptureAsync(repo, "profile-1", progress: progress);

        Assert.False(outcome.Changed);
        Assert.Empty(progress.Reports);
    }

    [Fact]
    public async Task Deleted_files_count_as_done()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.WriteMany(repo, "Data", 10);
        await _ws.CaptureAsync(repo, "profile-1");
        _ws.Delete(repo, "Data/f3.txt");
        _ws.Write(repo, "added.txt", "a");
        var progress = new ProgressLog();

        await _ws.CaptureAsync(repo, "profile-1", progress: progress);

        var last = progress.Last;
        Assert.Equal(2, last.FilesDone);
        Assert.Equal(2, last.FilesTotal);
    }

    [Fact]
    public async Task A_capture_with_nothing_to_record_reports_nothing()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.WriteMany(repo, "Data", 5);
        await _ws.CaptureAsync(repo, "profile-1");
        var progress = new ProgressLog();

        var outcome = await _ws.CaptureAsync(repo, "profile-1", progress: progress);

        Assert.False(outcome.Changed);
        Assert.Empty(progress.Reports);
    }

    [Theory]
    [InlineData("it's here.txt")]
    [InlineData("caf\u00e9/cr\u00e8me br\u00fbl\u00e9e.txt")]
    [InlineData("deep/er/path with spaces/file.txt")]
    public async Task The_current_path_is_the_real_path_not_a_mangled_one(string path)
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, path, "x");
        var progress = new ProgressLog();

        await _ws.CaptureAsync(repo, "profile-1", progress: progress);

        Assert.Equal(path, progress.Last.CurrentPath);
    }

    [Fact]
    public async Task Reports_are_throttled_but_the_last_one_always_arrives()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.WriteMany(repo, "Data", 300);
        var progress = new ProgressLog();

        await _ws.CaptureAsync(repo, "profile-1", progress: progress);

        Assert.True(progress.Reports.Count < 300, $"{progress.Reports.Count} reports for 300 files is not throttled");
        Assert.Equal(300, progress.Last.FilesDone);
    }
}
