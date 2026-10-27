using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// Marking a pack is a safeguard for the optimization that follows; a failure to mark must neither change how a capture ends nor
// leave an optimization throwing something the job does not report.
[Collection(EngineCollections.E)]
public sealed class StreamedPackFailureTests : IDisposable
{
    private const int _bigBytes = 5_000;

    private Action<string>? _writeKeep;
    private readonly EngineWorkspace _ws;

    public StreamedPackFailureTests() =>
        _ws = new(options => options with
        {
            StagingLimits = new StagingLimits(BatchBytes: 100_000, BatchFiles: 50, BigBytes: _bigBytes),
            WriteKeep = path => (_writeKeep ?? (_ => { }))(path),
        });

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    private static string PackFolder(RepoRef repo) => Path.Combine(repo.GitDir, "objects", "pack");

    private static string[] Packs(RepoRef repo) =>
        Directory.Exists(PackFolder(repo)) ? [.. Directory.GetFiles(PackFolder(repo), "*.pack").Select(Path.GetFileName).OfType<string>()] : [];

    private static string[] Keeps(RepoRef repo) =>
        Directory.Exists(PackFolder(repo)) ? [.. Directory.GetFiles(PackFolder(repo), "*.keep").Select(Path.GetFileName).OfType<string>()] : [];

    private static byte[] Bulk(int seed)
    {
        var bytes = new byte[_bigBytes * 4];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    private static void WriteKeepFile(string path) => File.WriteAllText(path, "SavePoint: test\n");

    [Fact]
    public async Task A_keep_that_cannot_be_written_does_not_change_how_the_capture_ends_and_the_next_repack_still_protects_the_pack()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "Data/big.bin", Bulk(1));
        _writeKeep = _ => throw new IOException("The disk is full.");

        var outcome = await _ws.CaptureAsync(repo, "profile-1");

        Assert.NotNull(outcome);
        Assert.NotNull(await _ws.TipAsync(repo, "profile-1"));
        Assert.Empty(Keeps(repo));
        var streamed = Assert.Single(Packs(repo));
        var bytes = await File.ReadAllBytesAsync(Path.Combine(PackFolder(repo), streamed), Ct);
        _writeKeep = WriteKeepFile;
        for (var round = 0; round < 3; round++)
        {
            await _ws.Engine.RepackAsync(repo, OptimizationMode.Routine, Ct);
            _ws.Write(repo, "Saves/slot.txt", "save " + round);
            await _ws.CaptureAsync(repo, "profile-1");
        }

        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(PackFolder(repo), streamed), Ct));
    }

    [Fact]
    public async Task A_keep_that_cannot_be_written_at_a_routine_repack_is_a_reported_failure_not_a_raw_exception()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "Data/big.bin", Bulk(1));
        _writeKeep = WriteKeepFile;
        await _ws.CaptureAsync(repo, "profile-1");
        foreach (var keep in Keeps(repo))
        {
            File.Delete(Path.Combine(PackFolder(repo), keep));
        }

        _writeKeep = _ => throw new UnauthorizedAccessException("Access to the path is denied.");

        await Assert.ThrowsAsync<GitOperationException>(() => _ws.Engine.RepackAsync(repo, OptimizationMode.Routine, Ct));
    }

    [Fact]
    public async Task A_keep_that_cannot_be_removed_at_a_reclaim_is_a_reported_failure_not_a_raw_exception()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows refuses to delete a file another handle holds open");

        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "Saves/slot.txt", "save");
        await _ws.CaptureAsync(repo, "profile-1");
        await _ws.GitWriteAsync(repo, "checkout", "--quiet", "-b", "profile-2");
        _ws.Write(repo, "Data/big.bin", Bulk(1));
        _writeKeep = WriteKeepFile;
        await _ws.CaptureAsync(repo, "profile-2");

        // The keep is only removed from a pack that holds something nothing reaches, which is what the deleted profile leaves.
        await _ws.GitWriteAsync(repo, "checkout", "--quiet", "profile-1");
        await _ws.Engine.DeleteBranchAsync(repo, "profile-2", Ct);
        var keep = Path.Combine(PackFolder(repo), Assert.Single(Keeps(repo)));
        using var held = new FileStream(keep, FileMode.Open, FileAccess.Read, FileShare.Read);

        await Assert.ThrowsAsync<GitOperationException>(() => _ws.Engine.RepackAsync(repo, OptimizationMode.Reclaim, Ct));
    }
}
