using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// A file over the big-file size is streamed into a pack of its own at capture. A geometric repack weighs packs by object count, so
// that pack always looks light and would be copied into a new one; the routine repack must leave it where it is.
[Collection(EngineCollections.E)]
public sealed class StreamedPackTests : IDisposable
{
    private const int _bigBytes = 5_000;

    private readonly EngineWorkspace _ws = new(options => options with { StagingLimits = new StagingLimits(BatchBytes: 100_000, BatchFiles: 50, BigBytes: _bigBytes) });

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    private static string PackFolder(RepoRef repo) => Path.Combine(repo.GitDir, "objects", "pack");

    private static string[] Packs(RepoRef repo) => Directory.Exists(PackFolder(repo))
        ? [.. Directory.GetFiles(PackFolder(repo), "*.pack").Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)]
        : [];

    private static string[] Keeps(RepoRef repo) => Directory.Exists(PackFolder(repo))
        ? [.. Directory.GetFiles(PackFolder(repo), "*.keep").Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)]
        : [];

    private static byte[] Bulk(int seed)
    {
        var bytes = new byte[_bigBytes * 4];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    // Writes one small file and one big one, captures, and returns the pack the capture streamed (the only pack that exists before a repack).
    private async Task<string> CaptureWithBigFileAsync(RepoRef repo, string branch, int round)
    {
        var before = Packs(repo);
        _ws.Write(repo, "Saves/slot.txt", "save " + round);
        _ws.Write(repo, "Data/big-" + branch + ".bin", Bulk(round));
        await _ws.CaptureAsync(repo, branch);
        var streamed = Assert.Single(Packs(repo).Except(before));
        Assert.Contains(Path.ChangeExtension(streamed, ".keep"), Keeps(repo));
        return streamed;
    }

    [Fact]
    public async Task A_capture_streamed_pack_survives_every_routine_repack_unchanged_while_the_small_objects_are_packed()
    {
        var repo = await _ws.InitializedRepoAsync();
        var streamed = new Dictionary<string, byte[]>();

        for (var round = 1; round <= 5; round++)
        {
            var pack = await CaptureWithBigFileAsync(repo, "profile-1", round);
            streamed[pack] = await File.ReadAllBytesAsync(Path.Combine(PackFolder(repo), pack), Ct);
            await _ws.Engine.RepackAsync(repo, OptimizationMode.Routine, Ct);
        }

        foreach (var (pack, bytes) in streamed)
        {
            Assert.True(File.Exists(Path.Combine(PackFolder(repo), pack)), $"{pack} was rewritten by a routine repack");
            Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(PackFolder(repo), pack), Ct));
        }

        Assert.Equal(0, await _ws.Engine.CountLooseObjectsAsync(repo, Ct));
        Assert.True(Packs(repo).Length > streamed.Count, "the small objects were not packed");
        Assert.Equal(0, (await _ws.GitAsync(repo, "fsck", "--no-dangling")).ExitCode);
    }

    [Fact]
    public async Task Free_up_space_removes_a_big_file_no_snapshot_reaches_keeps_the_ones_that_are_reached_and_leaves_no_stale_keep()
    {
        var repo = await _ws.InitializedRepoAsync();
        await CaptureWithBigFileAsync(repo, "profile-1", 1);
        await _ws.GitWriteAsync(repo, "checkout", "--quiet", "-b", "profile-2");
        await CaptureWithBigFileAsync(repo, "profile-2", 2);
        await _ws.Engine.RepackAsync(repo, OptimizationMode.Routine, Ct);
        var reached = await _ws.GitOutAsync(repo, "rev-parse", "profile-1:Data/big-profile-1.bin");
        var orphan = await _ws.GitOutAsync(repo, "rev-parse", "profile-2:Data/big-profile-2.bin");
        await _ws.GitWriteAsync(repo, "checkout", "--quiet", "profile-1");
        await _ws.Engine.DeleteBranchAsync(repo, "profile-2", Ct);
        await _ws.Engine.RepackAsync(repo, OptimizationMode.Routine, Ct);
        Assert.NotEmpty(Keeps(repo));
        Assert.Equal(new[] { true, true }, await _ws.ObjectsExistAsync(repo, reached, orphan));

        await _ws.Engine.RepackAsync(repo, OptimizationMode.Reclaim, Ct);

        Assert.Equal(new[] { true, false }, await _ws.ObjectsExistAsync(repo, reached, orphan));
        Assert.Empty(Keeps(repo));
        Assert.Single(Packs(repo));
    }

    [Fact]
    public async Task A_capture_that_streams_nothing_marks_no_pack()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.WriteMany(repo, "Data", 5);

        await _ws.CaptureAsync(repo, "profile-1");
        Assert.Empty(Packs(repo));
        await _ws.Engine.RepackAsync(repo, OptimizationMode.Routine, Ct);
        _ws.Write(repo, "Data/more.txt", "more");
        await _ws.CaptureAsync(repo, "profile-1");
        await _ws.Engine.RepackAsync(repo, OptimizationMode.Routine, Ct);

        Assert.Empty(Keeps(repo));
        Assert.NotEmpty(Packs(repo));
    }

    [Fact]
    public async Task Free_up_space_removes_a_keep_a_crash_left_empty_and_leaves_one_it_did_not_write()
    {
        var repo = await _ws.InitializedRepoAsync();
        var first = await CaptureWithBigFileAsync(repo, "profile-1", 1);
        var second = await CaptureWithBigFileAsync(repo, "profile-1", 2);
        var foreign = Path.Combine(PackFolder(repo), Path.ChangeExtension(second, ".keep"));
        await File.WriteAllTextAsync(Path.Combine(PackFolder(repo), Path.ChangeExtension(first, ".keep")), string.Empty, Ct);
        await File.WriteAllTextAsync(foreign, "kept by someone else\n", Ct);

        await _ws.Engine.RepackAsync(repo, OptimizationMode.Reclaim, Ct);

        Assert.Equal([Path.ChangeExtension(second, ".keep")], Keeps(repo));
        Assert.Equal("kept by someone else\n", await File.ReadAllTextAsync(foreign, Ct));
        Assert.DoesNotContain(first, Packs(repo));
        Assert.Contains(second, Packs(repo));
    }

    // 1,100 files are more objects than the sweep treats as a streamed pack, so only the capture's own keep can protect this one.
    [Fact]
    public async Task A_pack_with_more_streamed_files_than_the_sweep_trusts_is_still_kept_by_the_capture()
    {
        var repo = await _ws.InitializedRepoAsync();
        for (var i = 0; i < 1_100; i++)
        {
            _ws.Write(repo, $"Data/big-{i}.bin", Bulk(i));
        }

        await _ws.CaptureAsync(repo, "profile-1");
        var streamed = Assert.Single(Packs(repo));
        var bytes = await File.ReadAllBytesAsync(Path.Combine(PackFolder(repo), streamed), Ct);
        for (var round = 0; round < 2; round++)
        {
            // Each repack packs more objects than the streamed pack holds, which makes it the lightest and the one geometric repacking rolls up.
            for (var i = 0; i < 1_500; i++)
            {
                _ws.Write(repo, $"Saves{round}/slot-{i}.txt", $"save {round} number {i}");
            }

            await _ws.CaptureAsync(repo, "profile-1");
            await _ws.Engine.RepackAsync(repo, OptimizationMode.Routine, Ct);
        }

        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(PackFolder(repo), streamed), Ct));
    }

    [Fact]
    public async Task A_pack_of_many_small_files_that_grows_past_the_big_size_stays_in_geometric_repacking()
    {
        var repo = await _ws.InitializedRepoAsync();
        for (var i = 0; i < 1_100; i++)
        {
            _ws.Write(repo, $"Saves/slot-{i}.txt", "save number " + i);
        }

        await _ws.CaptureAsync(repo, "profile-1");
        await _ws.Engine.RepackAsync(repo, OptimizationMode.Routine, Ct);
        var merged = Assert.Single(Packs(repo));
        Assert.True(new FileInfo(Path.Combine(PackFolder(repo), merged)).Length > _bigBytes, "the scenario needs a small-file pack over the big size");
        _ws.Write(repo, "Saves/extra.txt", "one more");
        await _ws.CaptureAsync(repo, "profile-1");

        await _ws.Engine.RepackAsync(repo, OptimizationMode.Routine, Ct);

        Assert.Empty(Keeps(repo));
    }

    [Fact]
    public async Task A_watched_capture_keeps_its_streamed_pack_too()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "Saves/slot.txt", "save");
        _ws.Write(repo, "Data/big.bin", Bulk(1));

        await _ws.CaptureAsync(repo, "profile-1", progress: new ProgressLog());

        var streamed = Assert.Single(Packs(repo));
        Assert.Equal([Path.ChangeExtension(streamed, ".keep")], Keeps(repo));
    }

    [Fact]
    public async Task A_store_made_before_streamed_packs_were_marked_keeps_them_through_routine_repacks()
    {
        var repo = await _ws.InitializedRepoAsync();
        var streamed = new Dictionary<string, byte[]>();
        for (var round = 1; round <= 4; round++)
        {
            var pack = await CaptureWithBigFileAsync(repo, "profile-1", round);
            streamed[pack] = await File.ReadAllBytesAsync(Path.Combine(PackFolder(repo), pack), Ct);
            foreach (var keep in Keeps(repo))
            {
                File.Delete(Path.Combine(PackFolder(repo), keep));
            }

            await _ws.Engine.RepackAsync(repo, OptimizationMode.Routine, Ct);
        }

        foreach (var (pack, bytes) in streamed)
        {
            Assert.True(File.Exists(Path.Combine(PackFolder(repo), pack)), $"{pack} was rewritten by a routine repack");
            Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(PackFolder(repo), pack), Ct));
        }
    }
}
