using System.Security.Cryptography;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// Free up space tells the player about how much space comes back before it runs. The figure counts only data that nothing kept reaches,
// so a file a kept snapshot also holds never adds to it, however far back in the kept history that snapshot is.
[Collection(EngineCollections.E)]
public sealed class FreedBytesEstimateTests : IDisposable
{
    private const int _sharedBytes = 200_000;
    private const int _onlyBytes = 300_000;

    // Stored data adds a little to a file's size (the folder listings and the commit); this is far less than the smallest file here.
    private const int _slack = 30_000;

    private readonly EngineWorkspace _ws = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    [Fact]
    public async Task Data_only_the_deleted_refs_reach_is_counted_by_its_stored_size()
    {
        var (repo, points) = await TwoPointsAsync();

        var bytes = await _ws.Engine.EstimateFreedBytesAsync(repo, [new ClearedRef("refs/heads/doomed", points[1])], Ct);

        Assert.InRange(bytes, _onlyBytes, _onlyBytes + _slack);
    }

    [Fact]
    public async Task A_file_the_kept_history_also_holds_is_not_counted()
    {
        var (repo, points) = await SharedWithKeptAsync(sharedIsOnTheEdge: true);

        var bytes = await _ws.Engine.EstimateFreedBytesAsync(repo, [new ClearedRef("refs/heads/doomed", points[^1])], Ct);

        Assert.InRange(bytes, _onlyBytes, _onlyBytes + _slack);
    }

    // The kept branch dropped the file in its last snapshot, so it is in the kept history but not in the kept tip or the snapshot before
    // the doomed ones; listing "what the doomed refs reach and the kept tips do not" would count it.
    [Fact]
    public async Task A_file_only_an_older_kept_snapshot_holds_is_not_counted()
    {
        var (repo, points) = await SharedWithKeptAsync(sharedIsOnTheEdge: false);

        var bytes = await _ws.Engine.EstimateFreedBytesAsync(repo, [new ClearedRef("refs/heads/doomed", points[^1])], Ct);

        Assert.InRange(bytes, _onlyBytes, _onlyBytes + _slack);
    }

    [Fact]
    public async Task What_the_estimate_leaves_out_is_what_a_reclaim_keeps_and_what_it_counts_is_what_a_reclaim_drops()
    {
        var (repo, points) = await SharedWithKeptAsync(sharedIsOnTheEdge: false);
        var shared = await _ws.GitOutAsync(repo, "rev-parse", points[0] + ":shared.bin");
        var only = await _ws.GitOutAsync(repo, "rev-parse", points[^1] + ":only.bin");

        await _ws.Engine.ClearExactAsync(repo, [new ClearedRef("refs/heads/doomed", points[^1])], Ct);
        await _ws.Engine.RepackAsync(repo, OptimizationMode.Reclaim, Ct);

        Assert.Equal(new[] { true, false }, await _ws.ObjectsExistAsync(repo, shared, only));
    }

    // A reclaim keeps what the folder's index names, so the estimate does too.
    [Fact]
    public async Task Data_the_folders_index_still_names_is_not_counted()
    {
        var (repo, points) = await TwoPointsAsync(indexFollowsKept: false);

        var bytes = await _ws.Engine.EstimateFreedBytesAsync(repo, [new ClearedRef("refs/heads/doomed", points[1])], Ct);

        Assert.InRange(bytes, 0, _slack);
    }

    [Fact]
    public async Task Nothing_is_counted_for_no_refs_a_ref_that_moved_since_it_was_found_or_the_branch_the_folder_is_on()
    {
        var (repo, points) = await TwoPointsAsync();

        var none = await _ws.Engine.EstimateFreedBytesAsync(repo, [], Ct);

        // The ref was found at the second snapshot and has been moved back to the first since.
        await MoveAsync(repo, "doomed", points[0]);
        var moved = await _ws.Engine.EstimateFreedBytesAsync(repo, [new ClearedRef("refs/heads/doomed", points[1])], Ct);
        var head = await _ws.Engine.EstimateFreedBytesAsync(repo, [new ClearedRef("refs/heads/profile-1", points[0])], Ct);
        var gone = await _ws.Engine.EstimateFreedBytesAsync(repo, [new ClearedRef("refs/heads/never-was", points[1])], Ct);

        Assert.Equal([0L, 0L, 0L, 0L], [none, moved, head, gone]);
    }

    [Fact]
    public async Task Asking_again_with_the_refs_unchanged_answers_the_same_without_walking_the_store_again()
    {
        var (repo, points) = await TwoPointsAsync();
        var doomed = new[] { new ClearedRef("refs/heads/doomed", points[1]) };
        var first = await _ws.Engine.EstimateFreedBytesAsync(repo, doomed, Ct);
        var walks = WalksSoFar();

        var second = await _ws.Engine.EstimateFreedBytesAsync(repo, doomed, Ct);

        Assert.Equal(first, second);
        Assert.Equal(walks, WalksSoFar());
    }

    [Fact]
    public async Task A_new_snapshot_that_holds_the_same_data_lowers_the_estimate_on_the_next_ask()
    {
        var (repo, points) = await TwoPointsAsync();
        var doomed = new[] { new ClearedRef("refs/heads/doomed", points[1]) };
        var before = await _ws.Engine.EstimateFreedBytesAsync(repo, doomed, Ct);
        var walks = WalksSoFar();

        // The folder still holds the big file, so the kept profile's next snapshot holds it too and it is no longer only the doomed ref's.
        await _ws.CaptureAsync(repo, "profile-1", "Kept again");
        var after = await _ws.Engine.EstimateFreedBytesAsync(repo, doomed, Ct);

        Assert.InRange(before, _onlyBytes, _onlyBytes + _slack);
        Assert.InRange(after, 0, _slack);
        Assert.True(WalksSoFar() > walks);
    }

    [Fact]
    public async Task A_cancelled_ask_stops_instead_of_answering()
    {
        var (repo, points) = await TwoPointsAsync();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _ws.Engine.EstimateFreedBytesAsync(repo, [new ClearedRef("refs/heads/doomed", points[1])], cancelled.Token));
    }

    private int WalksSoFar() => _ws.Invocations.Count(invocation => invocation.Arguments[0] == "rev-list");

    // profile-1 holds the first snapshot; "doomed" also holds a second, which adds a big file nothing else has.
    // The index follows the kept snapshot, as it does after a real switch, unless the test wants it left on the doomed one.
    private async Task<(RepoRef Repo, string[] Points)> TwoPointsAsync(bool indexFollowsKept = true)
    {
        var repo = await _ws.InitializedFreshAsync();
        _ws.Write(repo, "a.txt", "one");
        var first = (await _ws.CaptureAsync(repo, "profile-1", "First")).TipCommitId!;
        _ws.Write(repo, "only.bin", RandomNumberGenerator.GetBytes(_onlyBytes));
        var second = (await _ws.CaptureAsync(repo, "profile-1", "Second")).TipCommitId!;
        await MoveAsync(repo, "doomed", second);
        await MoveAsync(repo, "profile-1", first);
        if (indexFollowsKept)
        {
            await _ws.GitWriteAsync(repo, "read-tree", first);
        }

        return (repo, [first, second]);
    }

    // The kept profile holds a big shared file in its first snapshot. When it is on the edge, the second snapshot (kept too) still has it,
    // and the doomed snapshot after that adds a copy; otherwise the second snapshot drops it, and the doomed one brings it back.
    private async Task<(RepoRef Repo, string[] Points)> SharedWithKeptAsync(bool sharedIsOnTheEdge)
    {
        var repo = await _ws.InitializedFreshAsync();
        _ws.Write(repo, "a.txt", "one");
        _ws.Write(repo, "shared.bin", RandomNumberGenerator.GetBytes(_sharedBytes));
        var first = (await _ws.CaptureAsync(repo, "profile-1", "First")).TipCommitId!;
        var shared = File.ReadAllBytes(_ws.FileOf(repo, "shared.bin"));
        if (!sharedIsOnTheEdge)
        {
            _ws.Delete(repo, "shared.bin");
        }

        _ws.Write(repo, "a.txt", "two");
        var second = (await _ws.CaptureAsync(repo, "profile-1", "Second")).TipCommitId!;
        _ws.Write(repo, "shared.bin", shared);
        _ws.Write(repo, "only.bin", RandomNumberGenerator.GetBytes(_onlyBytes));
        var third = (await _ws.CaptureAsync(repo, "profile-1", "Third")).TipCommitId!;
        await MoveAsync(repo, "doomed", third);
        await MoveAsync(repo, "profile-1", second);
        await _ws.GitWriteAsync(repo, "read-tree", second);
        return (repo, [first, second, third]);
    }

    private Task MoveAsync(RepoRef repo, string branch, string commit) => _ws.GitWriteAsync(repo, "update-ref", "refs/heads/" + branch, commit);
}
