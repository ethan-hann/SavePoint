using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// Free up space rewrites only what it frees: a pack a capture streamed (it carries a keep) stays as it is while everything in it is
// still reachable, and is rewritten without its unreachable objects, or dropped, once something in it is not.
[Collection(EngineCollections.E)]
public sealed class ReclaimStreamedPackTests : IDisposable
{
    private const int _bigBytes = 5_000;
    private const long _floor = 100L * 1024 * 1024;

    private readonly EngineWorkspace _ws = new(options => options with { StagingLimits = new StagingLimits(BatchBytes: 100_000, BatchFiles: 50, BigBytes: _bigBytes) });

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    private static string PackFolder(RepoRef repo) => Path.Combine(repo.GitDir, "objects", "pack");

    private static string[] Files(RepoRef repo, string pattern) => Directory.Exists(PackFolder(repo))
        ? [.. Directory.GetFiles(PackFolder(repo), pattern).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)]
        : [];

    private static string[] Packs(RepoRef repo) => Files(repo, "*.pack");

    private static string[] Keeps(RepoRef repo) => Files(repo, "*.keep");

    private static byte[] Bulk(int seed)
    {
        var bytes = new byte[_bigBytes * 4];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    private static byte[] BytesOf(RepoRef repo, string pack) => File.ReadAllBytes(Path.Combine(PackFolder(repo), pack));

    // One small file and one big one on the folder's current branch; returns the pack that capture streamed.
    private async Task<string> CaptureWithBigFileAsync(RepoRef repo, string branch, int round)
    {
        var before = Packs(repo);
        _ws.Write(repo, "Saves/slot.txt", "save " + round);
        _ws.Write(repo, "Data/big-" + branch + ".bin", Bulk(round));
        await _ws.CaptureAsync(repo, branch);
        return Assert.Single(Packs(repo).Except(before));
    }

    private async Task AssertFsckCleanAsync(RepoRef repo) =>
        Assert.Equal(0, (await _ws.GitAsync(repo, "fsck", "--no-dangling")).ExitCode);

    private void AssertKeptAndUntouched(RepoRef repo, Dictionary<string, byte[]> expected)
    {
        foreach (var (pack, bytes) in expected)
        {
            Assert.True(File.Exists(Path.Combine(PackFolder(repo), pack)), $"{pack} was rewritten");
            Assert.Equal(bytes, BytesOf(repo, pack));
            Assert.Contains(Path.ChangeExtension(pack, ".keep"), Keeps(repo));
        }
    }

    [Fact]
    public async Task Free_up_space_with_nothing_to_free_leaves_every_streamed_pack_and_its_keep_as_they_are()
    {
        var repo = await _ws.InitializedRepoAsync();
        var streamed = new Dictionary<string, byte[]>();
        for (var round = 1; round <= 3; round++)
        {
            var pack = await CaptureWithBigFileAsync(repo, "profile-1", round);
            streamed[pack] = BytesOf(repo, pack);
        }

        await _ws.Engine.RepackAsync(repo, OptimizationMode.Routine, Ct);
        var everything = Packs(repo).ToDictionary(pack => pack, pack => BytesOf(repo, pack));
        var keeps = Keeps(repo);
        var from = _ws.Invocations.Count;

        await _ws.Engine.RepackAsync(repo, OptimizationMode.Reclaim, Ct);

        Assert.Equal(everything.Keys.Order(StringComparer.Ordinal), Packs(repo));
        Assert.All(everything, entry => Assert.Equal(entry.Value, BytesOf(repo, entry.Key)));
        Assert.Equal(keeps, Keeps(repo));
        AssertKeptAndUntouched(repo, streamed);
        Assert.DoesNotContain(_ws.Invocations.Skip(from), invocation => invocation.Arguments[0] == "repack");
        await AssertFsckCleanAsync(repo);
    }

    [Fact]
    public async Task Deleting_a_profile_that_alone_reached_a_big_file_drops_that_pack_and_leaves_the_other_streamed_packs_alone()
    {
        var repo = await _ws.InitializedRepoAsync();
        var first = await CaptureWithBigFileAsync(repo, "profile-1", 1);
        await _ws.GitWriteAsync(repo, "checkout", "--quiet", "-b", "profile-2");
        var second = await CaptureWithBigFileAsync(repo, "profile-2", 2);
        await _ws.GitWriteAsync(repo, "checkout", "--quiet", "-b", "profile-3", "profile-1");
        var third = await CaptureWithBigFileAsync(repo, "profile-3", 3);
        await _ws.Engine.RepackAsync(repo, OptimizationMode.Routine, Ct);
        var orphan = await _ws.GitOutAsync(repo, "rev-parse", "profile-2:Data/big-profile-2.bin");
        var others = new[] { first, third }.ToDictionary(pack => pack, pack => BytesOf(repo, pack));
        await _ws.Engine.DeleteBranchAsync(repo, "profile-2", Ct);

        await _ws.Engine.RepackAsync(repo, OptimizationMode.Reclaim, Ct);

        Assert.False((await _ws.ObjectsExistAsync(repo, orphan))[0]);
        Assert.DoesNotContain(second, Packs(repo));
        AssertKeptAndUntouched(repo, others);
        await AssertFsckCleanAsync(repo);
    }

    [Fact]
    public async Task A_streamed_pack_with_one_unreachable_file_is_rewritten_without_it_and_the_other_streamed_pack_is_left_alone()
    {
        var (repo, shared, other, kept, dropped) = await OneUnreachableFileInASharedPackAsync();
        var untouched = new Dictionary<string, byte[]> { [other] = BytesOf(repo, other) };

        await _ws.Engine.RepackAsync(repo, OptimizationMode.Reclaim, Ct);

        Assert.Equal(new[] { true, false }, await _ws.ObjectsExistAsync(repo, kept, dropped));
        Assert.DoesNotContain(shared, Packs(repo));
        AssertKeptAndUntouched(repo, untouched);
        await AssertFsckCleanAsync(repo);
    }

    private async Task<(RepoRef Repo, string Shared, string Other, string Kept, string Dropped)> OneUnreachableFileInASharedPackAsync()
    {
        var repo = await _ws.InitializedRepoAsync();

        // One add streams both files into one pack; profile-1 then points at a point that holds only the first of them.
        _ws.Write(repo, "a.bin", Bulk(1));
        _ws.Write(repo, "b.bin", Bulk(2));
        _ws.Write(repo, "slot.txt", "save");
        await _ws.CaptureAsync(repo, "profile-2");
        var shared = Assert.Single(Packs(repo));
        var both = await _ws.GitOutAsync(repo, "ls-tree", "profile-2");
        var withoutB = string.Concat(both.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(line => !line.EndsWith("\tb.bin", StringComparison.Ordinal)).Select(line => line + "\n"));
        var tree = (await _ws.Runner.RunAsync(new GitInvocation(["mktree"]) { GitDir = repo.GitDir, Stdin = withoutB }, Ct)).StandardOutput.Trim();
        var point = (await _ws.Runner.RunAsync(new GitInvocation(["commit-tree", tree, "-m", "Only a"]) { GitDir = repo.GitDir }, Ct)).StandardOutput.Trim();
        await _ws.GitWriteAsync(repo, "update-ref", "refs/heads/profile-1", point);
        await _ws.GitWriteAsync(repo, "checkout", "--quiet", "profile-1");
        var other = await CaptureWithBigFileAsync(repo, "profile-1", 3);
        await _ws.Engine.RepackAsync(repo, OptimizationMode.Routine, Ct);
        var kept = await _ws.GitOutAsync(repo, "rev-parse", "profile-1:a.bin");
        var dropped = await _ws.GitOutAsync(repo, "rev-parse", "profile-2:b.bin");
        await _ws.Engine.DeleteBranchAsync(repo, "profile-2", Ct);
        return (repo, shared, other, kept, dropped);
    }

    [Fact]
    public async Task A_reclaim_with_nothing_to_free_needs_only_the_free_space_floor_however_large_the_streamed_packs_are()
    {
        var repo = await _ws.InitializedRepoAsync();
        var packs = new Dictionary<string, byte[]>();
        for (var round = 1; round <= 3; round++)
        {
            var pack = await CaptureWithBigFileAsync(repo, "profile-1", round);
            packs[pack] = BytesOf(repo, pack);
        }

        await _ws.Engine.RepackAsync(repo, OptimizationMode.Routine, Ct);
        Assert.True(await _ws.Engine.GetRepoSizeAsync(repo, Ct) > 2 * _bigBytes, "the scenario needs a store larger than the space it is given");
        _ws.Space.Free = _floor;

        await _ws.Engine.CheckReclaimAsync(repo, Ct);
        await _ws.Engine.RepackAsync(repo, OptimizationMode.Reclaim, Ct);

        AssertKeptAndUntouched(repo, packs);
    }

    [Fact]
    public async Task A_reclaim_that_must_rewrite_a_streamed_pack_needs_room_for_that_pack_and_not_for_the_whole_store()
    {
        var (repo, shared, _, _, dropped) = await OneUnreachableFileInASharedPackAsync();
        var sharedBytes = new FileInfo(Path.Combine(PackFolder(repo), shared)).Length;
        var store = await _ws.Engine.GetRepoSizeAsync(repo, Ct);
        _ws.Space.Free = _floor;
        var from = _ws.Invocations.Count;

        var refused = await Assert.ThrowsAsync<DiskSpaceException>(() => _ws.Engine.RepackAsync(repo, OptimizationMode.Reclaim, Ct));
        var needed = (long)refused.Args["requiredBytes"]! - _floor;

        Assert.InRange(needed, sharedBytes, store - 1);
        Assert.DoesNotContain(_ws.Invocations.Skip(from), invocation => invocation.Arguments[0] is "reflog" or "repack" or "prune");
        _ws.Space.Free = _floor + needed;
        await _ws.Engine.RepackAsync(repo, OptimizationMode.Reclaim, Ct);
        Assert.False((await _ws.ObjectsExistAsync(repo, dropped))[0]);
    }

    [Fact]
    public async Task The_check_before_records_are_cleared_counts_a_kept_pack_only_when_clearing_them_makes_it_rewritable()
    {
        var repo = await _ws.InitializedRepoAsync();
        await CaptureWithBigFileAsync(repo, "profile-1", 1);
        var older = (await _ws.TipAsync(repo, "profile-1"))!;
        await CaptureWithBigFileAsync(repo, "profile-1", 2);
        await _ws.Engine.CheckoutCommitAsync(repo, older, ct: Ct);
        _ws.Write(repo, "played.txt", "progress made while playing");
        await _ws.Engine.CaptureAsync(repo, new CaptureRequest("profile-1", "Continued", "cap-move") { Mode = CaptureMode.MoveForward, ExpectedStartCommit = older }, ct: Ct);
        await _ws.Engine.RepackAsync(repo, OptimizationMode.Routine, Ct);
        var records = (await _ws.GitOutAsync(repo, "for-each-ref", "--format=%(refname)", "refs/savepoint/removed/")).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _ws.Space.Free = _floor;

        await _ws.Engine.CheckReclaimAsync(repo, [], Ct);
        var refused = await Assert.ThrowsAsync<DiskSpaceException>(() => _ws.Engine.CheckReclaimAsync(repo, records, Ct));

        Assert.True((long)refused.Args["requiredBytes"]! > _floor);
        Assert.NotEmpty(records);
    }

    [Fact]
    public async Task A_reclaim_with_only_loose_objects_to_pack_leaves_the_unkept_packs_alone()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.WriteMany(repo, "Saves", 20);
        await _ws.CaptureAsync(repo, "profile-1");
        await _ws.Engine.RepackAsync(repo, OptimizationMode.Routine, Ct);
        var packed = Assert.Single(Packs(repo));
        var bytes = BytesOf(repo, packed);
        _ws.Write(repo, "Saves/new.txt", "newer");
        await _ws.CaptureAsync(repo, "profile-1");
        Assert.True(await _ws.Engine.CountLooseObjectsAsync(repo, Ct) > 0);
        var from = _ws.Invocations.Count;

        await _ws.Engine.RepackAsync(repo, OptimizationMode.Reclaim, Ct);

        var repack = Assert.Single(_ws.Invocations.Skip(from), invocation => invocation.Arguments[0] == "repack");
        Assert.Equal(["repack", "-q", "-d"], repack.Arguments);
        Assert.Equal(bytes, BytesOf(repo, packed));
        Assert.Equal(0, await _ws.Engine.CountLooseObjectsAsync(repo, Ct));
        await AssertFsckCleanAsync(repo);
    }

    [Fact]
    public async Task A_recently_removed_snapshots_big_file_survives_free_up_space_only_while_its_record_exists()
    {
        var repo = await _ws.InitializedRepoAsync();
        var first = await CaptureWithBigFileAsync(repo, "profile-1", 1);
        var older = (await _ws.TipAsync(repo, "profile-1"))!;
        var second = await CaptureWithBigFileAsync(repo, "profile-1", 2);
        var removedFile = await _ws.GitOutAsync(repo, "rev-parse", "profile-1:Data/big-profile-1.bin");
        await _ws.Engine.CheckoutCommitAsync(repo, older, ct: Ct);
        _ws.Write(repo, "played.txt", "progress made while playing");
        await _ws.Engine.CaptureAsync(repo, new CaptureRequest("profile-1", "Continued", "cap-move") { Mode = CaptureMode.MoveForward, ExpectedStartCommit = older }, ct: Ct);
        await _ws.Engine.RepackAsync(repo, OptimizationMode.Routine, Ct);
        var firstBytes = BytesOf(repo, first);
        var secondBytes = BytesOf(repo, second);

        await _ws.Engine.RepackAsync(repo, OptimizationMode.Reclaim, Ct);

        Assert.True((await _ws.ObjectsExistAsync(repo, removedFile))[0], "the record's big file went while the record existed");
        AssertKeptAndUntouched(repo, new Dictionary<string, byte[]> { [first] = firstBytes, [second] = secondBytes });

        var records = (await _ws.GitOutAsync(repo, "for-each-ref", "--format=%(refname)", "refs/savepoint/removed/")).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        await _ws.Engine.ClearRemovedAsync(repo, records, Ct);
        await _ws.Engine.RepackAsync(repo, OptimizationMode.Reclaim, Ct);

        Assert.False((await _ws.ObjectsExistAsync(repo, removedFile))[0], "the big file stayed after its record went");
        Assert.DoesNotContain(second, Packs(repo));
        AssertKeptAndUntouched(repo, new Dictionary<string, byte[]> { [first] = firstBytes });
        await AssertFsckCleanAsync(repo);
    }

    private const string _ownKeepText = "SavePoint: holds files streamed at capture.\n";

    [Fact]
    public async Task A_pack_without_an_index_a_killed_add_left_behind_does_not_stop_a_reclaim_and_is_left_as_it_is()
    {
        var (repo, shared, other, _, dropped) = await OneUnreachableFileInASharedPackAsync();
        var orphan = "pack-" + new string('1', 40) + ".pack";
        await File.WriteAllBytesAsync(Path.Combine(PackFolder(repo), orphan), BytesOf(repo, other), Ct);
        await File.WriteAllTextAsync(Path.Combine(PackFolder(repo), Path.ChangeExtension(orphan, ".keep")), _ownKeepText, Ct);
        var orphanBytes = BytesOf(repo, orphan);

        await _ws.Engine.CheckReclaimAsync(repo, Ct);
        await _ws.Engine.RepackAsync(repo, OptimizationMode.Reclaim, Ct);

        Assert.False((await _ws.ObjectsExistAsync(repo, dropped))[0]);
        Assert.DoesNotContain(shared, Packs(repo));
        Assert.Equal(orphanBytes, BytesOf(repo, orphan));
        Assert.Contains(Path.ChangeExtension(orphan, ".keep"), Keeps(repo));
    }

    [Fact]
    public async Task A_keep_of_ours_whose_pack_is_gone_is_removed_even_when_nothing_is_unreachable()
    {
        var repo = await _ws.InitializedRepoAsync();
        var pack = await CaptureWithBigFileAsync(repo, "profile-1", 1);
        await _ws.Engine.RepackAsync(repo, OptimizationMode.Routine, Ct);
        var stale = Path.Combine(PackFolder(repo), "pack-" + new string('2', 40) + ".keep");
        await File.WriteAllTextAsync(stale, _ownKeepText, Ct);
        var bytes = BytesOf(repo, pack);

        await _ws.Engine.RepackAsync(repo, OptimizationMode.Reclaim, Ct);

        Assert.False(File.Exists(stale));
        AssertKeptAndUntouched(repo, new Dictionary<string, byte[]> { [pack] = bytes });
    }

    [Fact]
    public async Task A_reclaim_is_refused_while_a_switch_is_unfinished_and_changes_nothing()
    {
        var (repo, shared, _, _, dropped) = await OneUnreachableFileInASharedPackAsync();
        var tip = (await _ws.TipAsync(repo, "profile-1"))!;
        SwitchJournal.Write(repo.GitDir, null, tip);
        var from = _ws.Invocations.Count;

        await Assert.ThrowsAsync<InterruptedSwitchException>(() => _ws.Engine.CheckReclaimAsync(repo, Ct));
        await Assert.ThrowsAsync<InterruptedSwitchException>(() => _ws.Engine.RepackAsync(repo, OptimizationMode.Reclaim, Ct));

        Assert.DoesNotContain(_ws.Invocations.Skip(from), invocation => invocation.Arguments[0] is "reflog" or "repack" or "prune");
        Assert.Contains(shared, Packs(repo));
        Assert.True((await _ws.ObjectsExistAsync(repo, dropped))[0]);
    }

    [Fact]
    public async Task A_big_file_added_to_a_streamed_pack_and_never_committed_keeps_its_pack_through_a_reclaim()
    {
        var repo = await _ws.InitializedRepoAsync();
        await CaptureWithBigFileAsync(repo, "profile-1", 1);
        var before = Packs(repo);
        _ws.Write(repo, "Data/extra.bin", Bulk(9));

        // What a capture killed between its add and its commit leaves: the file staged in a pack of its own.
        await _ws.Runner.RunAsync(new GitInvocation(["-c", "core.bigFileThreshold=" + _bigBytes, "add", "-f", "Data/extra.bin"]) { GitDir = repo.GitDir, WorkTree = repo.WorkTree }, Ct);
        var staged = Assert.Single(Packs(repo).Except(before));
        await File.WriteAllTextAsync(Path.Combine(PackFolder(repo), Path.ChangeExtension(staged, ".keep")), _ownKeepText, Ct);
        var bytes = BytesOf(repo, staged);
        var blob = await _ws.GitOutAsync(repo, "rev-parse", ":Data/extra.bin");

        // Something unreachable, so the reclaim looks at what each kept pack holds.
        await _ws.Runner.RunAsync(new GitInvocation(["hash-object", "-w", "--stdin"]) { GitDir = repo.GitDir, Stdin = "nothing reaches this" }, Ct);

        await _ws.Engine.RepackAsync(repo, OptimizationMode.Reclaim, Ct);

        Assert.Equal(new[] { true }, await _ws.ObjectsExistAsync(repo, blob));
        AssertKeptAndUntouched(repo, new Dictionary<string, byte[]> { [staged] = bytes });
    }
}
