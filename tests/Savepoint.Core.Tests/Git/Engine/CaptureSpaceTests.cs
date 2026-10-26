using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// A snapshot has to leave room for the bytes it adds, and what an earlier failed one left behind must not count against it.
[Collection(EngineCollections.F)]
public sealed class CaptureSpaceTests : IDisposable
{
    private const long _floor = 1_000;

    private readonly EngineWorkspace _ws = new(options => options with { MinFreeBytesForStorageWrites = _floor });

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    private static int ObjectFiles(RepoRef repo) =>
        Directory.EnumerateFiles(Path.Combine(repo.GitDir, "objects"), "*", SearchOption.AllDirectories).Count();

    private static long TemporaryPackBytes(RepoRef repo)
    {
        var pack = Path.Combine(repo.GitDir, "objects", "pack");
        return Directory.Exists(pack) ? Directory.EnumerateFiles(pack, "tmp_pack_*").Sum(file => new FileInfo(file).Length) : 0;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_snapshot_larger_than_the_free_space_is_refused_before_any_object_is_written(bool watched)
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "big.bin", new byte[300_000]);
        _ws.Write(repo, "small.txt", "x");
        _ws.Space.Free = _floor + 200_000;
        var objectsBefore = ObjectFiles(repo);

        var error = await Assert.ThrowsAsync<DiskSpaceException>(() => _ws.CaptureAsync(repo, progress: watched ? new Progress<FileProgress>(_ => { }) : null));

        Assert.Equal("error.diskSpace.low", error.MessageId);
        Assert.Equal(_floor + 300_001, error.Args["requiredBytes"]);
        Assert.Equal(_floor + 200_000, error.Args["freeBytes"]);
        Assert.Equal(objectsBefore, ObjectFiles(repo));
        Assert.Null(await _ws.ResolveAsync(repo, "HEAD"));
    }

    [Fact]
    public async Task A_snapshot_that_fits_the_free_space_is_recorded()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "big.bin", new byte[300_000]);
        _ws.Space.Free = _floor + 300_000;

        var outcome = await _ws.CaptureAsync(repo);

        Assert.True(outcome.Changed);
    }

    [Fact]
    public async Task Only_the_files_that_changed_count_toward_the_space_a_snapshot_needs()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "big.bin", new byte[300_000]);
        await _ws.CaptureAsync(repo, "profile-1", "First");
        _ws.Write(repo, "small.txt", "x");
        _ws.Space.Free = _floor + 5_000;

        var outcome = await _ws.CaptureAsync(repo, "profile-1", "Second");

        Assert.True(outcome.Changed);
    }

    [Fact]
    public async Task A_temporary_pack_a_stopped_snapshot_left_does_not_block_the_next_one()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");
        var leftover = Path.Combine(repo.GitDir, "objects", "pack", "tmp_pack_stopped");
        Directory.CreateDirectory(Path.GetDirectoryName(leftover)!);
        await File.WriteAllBytesAsync(leftover, new byte[50_000], Ct);

        // The volume is as full as the leftover makes it: room for the snapshot only once the leftover is gone.
        _ws.Space.PerPath = _ => _floor + 10 - TemporaryPackBytes(repo);

        var outcome = await _ws.CaptureAsync(repo);

        Assert.True(outcome.Changed);
        Assert.False(File.Exists(leftover));
    }

    [Fact]
    public async Task A_snapshot_is_still_refused_when_the_volume_stays_low_after_the_leftover_is_cleared()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");
        var leftover = Path.Combine(repo.GitDir, "objects", "pack", "tmp_pack_stopped");
        Directory.CreateDirectory(Path.GetDirectoryName(leftover)!);
        await File.WriteAllBytesAsync(leftover, new byte[50_000], Ct);
        _ws.Space.Free = 10;

        var error = await Assert.ThrowsAsync<DiskSpaceException>(() => _ws.CaptureAsync(repo));

        Assert.Equal(_floor, error.Args["requiredBytes"]);
        Assert.False(File.Exists(leftover), "the leftover was meant to be cleared before the volume was judged again");
        Assert.Null(await _ws.ResolveAsync(repo, "HEAD"));
    }
}
