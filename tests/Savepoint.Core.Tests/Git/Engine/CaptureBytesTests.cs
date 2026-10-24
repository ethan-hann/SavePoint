using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// A capture says how many bytes it has to store before it stores any, so a large file moves the bar instead of leaving it blank.
[Collection(EngineCollections.D)]
public sealed class CaptureBytesTests : IDisposable
{
    private EngineWorkspace _ws = new();

    public void Dispose() => _ws.Dispose();

    [Fact]
    public async Task The_first_report_gives_the_file_and_byte_totals_before_any_file_is_handled()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "Data/a.bin", new byte[1000]);
        _ws.Write(repo, "Data/b.bin", new byte[2000]);
        var progress = new ProgressLog();

        await _ws.CaptureAsync(repo, "profile-1", progress: progress);

        Assert.Equal(new FileProgress(0, 2, null, 0, 3000), progress.Reports[0]);
    }

    [Fact]
    public async Task The_last_report_has_every_byte_written()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "Data/a.bin", new byte[1000]);
        _ws.Write(repo, "Data/b.bin", new byte[2000]);
        var progress = new ProgressLog();

        await _ws.CaptureAsync(repo, "profile-1", progress: progress);

        Assert.Equal(2, progress.Last.FilesDone);
        Assert.Equal(3000, progress.Last.BytesDone);
        Assert.Equal(3000, progress.Last.BytesTotal);
    }

    [Fact]
    public async Task A_later_capture_counts_only_the_bytes_of_the_files_it_stores()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "Data/a.bin", new byte[1000]);
        _ws.Write(repo, "Data/b.bin", new byte[2000]);
        await _ws.CaptureAsync(repo, "profile-1");
        _ws.Write(repo, "Data/a.bin", new byte[500]);
        _ws.Delete(repo, "Data/b.bin");
        var progress = new ProgressLog();

        await _ws.CaptureAsync(repo, "profile-1", progress: progress);

        Assert.Equal(500, progress.Reports[0].BytesTotal);
        Assert.Equal(500, progress.Last.BytesDone);
    }

    [Fact]
    public async Task Bytes_the_object_store_grew_by_while_git_worked_are_reported_before_the_capture_ends()
    {
        var grew = false;
        _ws.Dispose();
        _ws = new EngineWorkspace(options =>
        {
            var inner = options.InvocationObserver!;
            return options with
            {
                InvocationObserver = invocation =>
                {
                    inner(invocation);
                    if (invocation.Arguments[0] == "add")
                    {
                        Volatile.Write(ref grew, true);
                    }
                },
                ObjectStoreSize = _ => Volatile.Read(ref grew) ? 400 : 0,
                SampleWait = ct => Task.Delay(Timeout.Infinite, ct),
            };
        });
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "Data/a.bin", new byte[1000]);
        var progress = new ProgressLog();

        await _ws.CaptureAsync(repo, "profile-1", progress: progress);

        Assert.Contains(progress.Reports, report => report.BytesDone == 400 && report.BytesTotal == 1000);
    }

    [Fact]
    public async Task A_store_that_has_all_its_bytes_and_goes_quiet_while_git_works_is_reported_as_finishing()
    {
        var added = false;
        _ws.Dispose();
        _ws = new EngineWorkspace(options =>
        {
            var inner = options.InvocationObserver!;
            return options with
            {
                // git is held for half a second after the store has its bytes, standing in for the wait at the end of a very large capture.
                InvocationObserver = invocation =>
                {
                    inner(invocation);
                    if (invocation.Arguments[0] == "add")
                    {
                        Volatile.Write(ref added, true);
                        Thread.Sleep(500);
                    }
                },
                ObjectStoreSize = _ => Volatile.Read(ref added) ? 1000 : 0,
                SampleWait = ct => Task.Delay(20, ct),
            };
        });
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "Data/a.bin", new byte[1000]);
        var progress = new ProgressLog();

        await _ws.CaptureAsync(repo, "profile-1", progress: progress);

        var finishing = Assert.Single(progress.Reports.Where(report => report.Finishing).Take(1));
        Assert.True(finishing.BytesDone < finishing.BytesTotal, "the figure stays short of the total until the capture is done");
        Assert.False(progress.Last.Finishing);
        Assert.Equal(1000, progress.Last.BytesDone);
    }
}
