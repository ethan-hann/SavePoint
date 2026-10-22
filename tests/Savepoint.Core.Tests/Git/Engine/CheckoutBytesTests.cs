using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// A switch reports the bytes it has written as well as the files, so the progress dialog can show "3.8 GB of 6.1 GB".
[Collection(EngineCollections.F)]
public sealed class CheckoutBytesTests : IDisposable
{
    private readonly EngineWorkspace _ws = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    [Fact]
    public async Task Bytes_done_follow_the_order_git_writes_the_files_and_end_at_the_total()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "Data/keep.bin", new string('k', 300));
        _ws.Write(repo, "Data/a-new-in-a.bin", new string('n', 700));
        _ws.Write(repo, "Data/m-changed.bin", new string('a', 1500));
        await _ws.CaptureAsync(repo, "profile-a");
        await _ws.GitWriteAsync(repo, "checkout", "--quiet", "-b", "profile-b");
        _ws.Delete(repo, "Data/a-new-in-a.bin");
        _ws.Write(repo, "Data/m-changed.bin", new string('b', 90));
        _ws.Write(repo, "Data/z-only-in-b.bin", new string('z', 5));
        await _ws.CaptureAsync(repo, "profile-b");
        var progress = new ProgressLog();

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", progress, Ct);

        // git removes first (z-only-in-b), then writes in path order (a-new-in-a, then m-changed); keep.bin is not touched.
        long[] bytesAfter = [0, 0, 700, 2200];
        var reports = progress.Reports;
        Assert.NotEmpty(reports);
        Assert.All(reports, report =>
        {
            Assert.Equal(2200, report.BytesTotal);
            Assert.Equal(bytesAfter[report.FilesDone], report.BytesDone);
        });
        Assert.Equal(new FileProgress(3, 3, null, 2200, 2200), progress.Last);
    }

    [Fact]
    public async Task A_switch_that_removes_many_files_counts_them_while_the_bytes_wait_at_zero()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "Data/base.bin", "base");
        await _ws.CaptureAsync(repo, "profile-a");
        await _ws.GitWriteAsync(repo, "checkout", "--quiet", "-b", "profile-b");
        _ws.WriteMany(repo, "Mods", 200, content: "mod file");
        _ws.Write(repo, "Data/base.bin", "base, patched");
        await _ws.CaptureAsync(repo, "profile-b");
        var progress = new ProgressLog();

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", progress, Ct);

        // 200 removals, then the one file that changed: the file count moves through the removals, and no byte is done until the write.
        var removing = progress.Reports.Where(report => report.FilesDone is > 0 and < 200).ToList();
        Assert.NotEmpty(removing);
        Assert.All(removing, report => Assert.Equal(0, report.BytesDone));
        Assert.All(progress.Reports, report => Assert.Equal(201, report.FilesTotal));
        Assert.Equal(new FileProgress(201, 201, null, 4, 4), progress.Last);
    }

    [Fact]
    public async Task A_switch_that_only_removes_files_has_no_bytes_to_write()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "Data/keep.bin", "keep");
        await _ws.CaptureAsync(repo, "profile-a");
        await _ws.GitWriteAsync(repo, "checkout", "--quiet", "-b", "profile-b");
        _ws.Write(repo, "Data/extra.bin", new string('x', 400));
        await _ws.CaptureAsync(repo, "profile-b");
        var progress = new ProgressLog();

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", progress, Ct);

        Assert.Equal(new FileProgress(1, 1, null, 0, 0), progress.Last);
    }
}
