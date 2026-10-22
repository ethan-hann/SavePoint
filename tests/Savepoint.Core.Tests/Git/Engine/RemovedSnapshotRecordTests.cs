using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// A save that takes snapshots off a branch (continuing from an older one, or replacing the latest) keeps each one reachable through
// a record outside every branch, written before the branch moves, so no stop can leave a snapshot removed and unrecorded.
[Collection(EngineCollections.E)]
public sealed class RemovedSnapshotRecordTests : IDisposable
{
    private static readonly DateTimeOffset _at = new(2026, 7, 4, 12, 30, 15, 250, TimeSpan.Zero);

    private readonly EngineWorkspace _ws = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    [Fact]
    public async Task A_move_forward_lists_each_snapshot_it_drops_as_removed_from_that_branch()
    {
        var (repo, points) = await PlayingFromTheFirstPointAsync();

        await _ws.Engine.CaptureAsync(repo, Move("profile-1", points[0], "Continued", "cap-move"), ct: Ct);

        var removed = await _ws.Engine.ListRemovedAsync(repo, Ct);
        Assert.Equal([points[2], points[1]], removed.Select(entry => entry.Commit.Id));
        Assert.All(removed, entry =>
        {
            Assert.Equal("profile-1", entry.Branch);
            Assert.Equal(RemovalReason.MovedBack, entry.Reason);
            Assert.Equal(_at, entry.RemovedAt);
            Assert.NotNull(entry.Commit.CaptureId);
        });
        Assert.Equal(["Third point", "Second point"], removed.Select(entry => entry.Commit.Subject));
    }

    [Fact]
    public async Task A_removed_snapshot_is_on_no_branch_and_no_branch_listing_shows_it()
    {
        var (repo, points) = await PlayingFromTheFirstPointAsync();

        await _ws.Engine.CaptureAsync(repo, Move("profile-1", points[0], "Continued", "cap-move"), ct: Ct);

        var lineage = (await _ws.Engine.ListCommitsAsync(repo, "profile-1", ct: Ct)).Select(commit => commit.Id).ToList();
        Assert.Equal(["profile-1"], await _ws.Engine.ListBranchesAsync(repo, Ct));
        Assert.Equal(2, lineage.Count);
        Assert.DoesNotContain(points[1], lineage);
        Assert.DoesNotContain(points[2], lineage);
        Assert.DoesNotContain(points[2], (await _ws.Engine.ListCommitsOnlyOnAsync(repo, "profile-1", [], Ct)).Select(commit => commit.Id));
    }

    [Fact]
    public async Task A_move_forward_that_drops_nothing_records_nothing()
    {
        var (repo, points) = await _ws.ThreePointsRepoAsync();
        _ws.Write(repo, "c.txt", "c");

        await _ws.Engine.CaptureAsync(repo, Move("profile-1", points[2], "Next", "cap-next"), ct: Ct);

        Assert.Empty(await _ws.Engine.ListRemovedAsync(repo, Ct));
        Assert.Empty(await RawRecordsAsync(repo));
    }

    [Fact]
    public async Task The_snapshots_a_move_forward_drops_survive_a_routine_run_and_a_reclaim()
    {
        var (repo, points) = await PlayingFromTheFirstPointAsync();
        await _ws.Engine.CaptureAsync(repo, Move("profile-1", points[0], "Continued", "cap-move"), ct: Ct);

        await _ws.Engine.RepackAsync(repo, OptimizationMode.Routine, Ct);
        await _ws.Engine.RepackAsync(repo, OptimizationMode.Reclaim, Ct);

        Assert.Equal(new[] { true, true }, await _ws.ObjectsExistAsync(repo, points[1], points[2]));
        Assert.Equal([points[2], points[1]], (await _ws.Engine.ListRemovedAsync(repo, Ct)).Select(entry => entry.Commit.Id));
    }

    [Fact]
    public async Task A_removed_snapshot_a_branch_reaches_again_is_marked_as_on_a_branch_and_its_record_stays()
    {
        var (repo, points) = await PlayingFromTheFirstPointAsync();
        await _ws.Engine.CaptureAsync(repo, Move("profile-1", points[0], "Continued", "cap-move"), ct: Ct);

        await _ws.Engine.CreateBranchAsync(repo, "brought-back", points[1], Ct);

        var removed = await _ws.Engine.ListRemovedAsync(repo, Ct);
        Assert.Equal([points[2]], removed.Where(entry => !entry.OnBranch).Select(entry => entry.Commit.Id));
        Assert.Equal([points[1]], removed.Where(entry => entry.OnBranch).Select(entry => entry.Commit.Id));
        Assert.Equal(2, (await RawRecordsAsync(repo)).Count);
    }

    [Fact]
    public async Task A_move_forward_stopped_before_the_records_are_written_has_recorded_nothing_and_moved_nothing()
    {
        var (repo, points) = await PlayingFromTheFirstPointAsync();
        _ws.OnInvoke = invocation => FailAt(invocation, IsRecordWrite);

        await Assert.ThrowsAsync<IOException>(() => _ws.Engine.CaptureAsync(repo, Move("profile-1", points[0], "Continued", "cap-move"), ct: Ct));

        Assert.Empty(await RawRecordsAsync(repo));
        Assert.Equal(points[2], await _ws.TipAsync(repo, "profile-1"));
        Assert.Null(await _ws.HeadRefAsync(repo));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_move_forward_stopped_after_the_records_and_before_the_branch_moves_has_lost_nothing_and_a_repeat_finishes_it(bool byCancel)
    {
        var (repo, points) = await PlayingFromTheFirstPointAsync();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        _ws.OnInvoke = invocation =>
        {
            if (IsBranchMove(invocation, "profile-1"))
            {
                StopNow(cancel, byCancel);
            }
        };
        var request = Move("profile-1", points[0], "Continued", "cap-move");

        var stopped = _ws.Engine.CaptureAsync(repo, request, ct: cancel.Token);
        if (byCancel)
        {
            await Assert.ThrowsAsync<OperationCancelledByUserException>(() => stopped);
        }
        else
        {
            await Assert.ThrowsAsync<IOException>(() => stopped);
        }

        // Every snapshot is still on the branch, and each also has its record, so a later reclaim cannot drop one.
        Assert.Equal(points[2], await _ws.TipAsync(repo, "profile-1"));
        Assert.Equal(2, (await RawRecordsAsync(repo)).Count);
        var recorded = await _ws.Engine.ListRemovedAsync(repo, Ct);
        Assert.Equal(2, recorded.Count);
        Assert.All(recorded, entry => Assert.True(entry.OnBranch));
        await _ws.Engine.RepackAsync(repo, OptimizationMode.Reclaim, Ct);
        Assert.Equal(new[] { true, true }, await _ws.ObjectsExistAsync(repo, points[1], points[2]));

        _ws.OnInvoke = null;
        await _ws.Engine.CaptureAsync(repo, request, ct: Ct);

        Assert.Equal([points[2], points[1]], (await _ws.Engine.ListRemovedAsync(repo, Ct)).Select(entry => entry.Commit.Id));
        Assert.Equal(2, (await RawRecordsAsync(repo)).Count);
    }

    [Fact]
    public async Task A_move_forward_stopped_after_the_branch_moved_and_before_head_followed_still_has_every_record()
    {
        var (repo, points) = await PlayingFromTheFirstPointAsync();
        _ws.OnInvoke = invocation => FailAt(invocation, candidate => candidate.Arguments[0] == "symbolic-ref");

        await Assert.ThrowsAsync<IOException>(() => _ws.Engine.CaptureAsync(repo, Move("profile-1", points[0], "Continued", "cap-move"), ct: Ct));

        Assert.Equal([points[2], points[1]], (await _ws.Engine.ListRemovedAsync(repo, Ct)).Select(entry => entry.Commit.Id));
    }

    [Fact]
    public async Task An_amend_lists_the_commit_it_replaces_as_removed_from_that_branch()
    {
        var (repo, points) = await _ws.ThreePointsRepoAsync();
        _ws.Write(repo, "c.txt", "added later");

        var outcome = await _ws.Engine.CaptureAsync(repo, Amend("profile-1", "Third point, updated", "cap-new"), ct: Ct);

        var removed = Assert.Single(await _ws.Engine.ListRemovedAsync(repo, Ct));
        Assert.Equal(points[2], removed.Commit.Id);
        Assert.Equal("Third point", removed.Commit.Subject);
        Assert.Equal("profile-1", removed.Branch);
        Assert.Equal(RemovalReason.Replaced, removed.Reason);
        Assert.Equal(_at, removed.RemovedAt);
        Assert.NotEqual(points[2], outcome.TipCommitId);
        Assert.DoesNotContain(points[2], (await _ws.Engine.ListCommitsAsync(repo, "profile-1", ct: Ct)).Select(commit => commit.Id));
    }

    [Fact]
    public async Task An_amend_skipped_because_nothing_changed_records_nothing()
    {
        var (repo, _) = await _ws.ThreePointsRepoAsync();

        await _ws.Engine.CaptureAsync(repo, Amend("profile-1", "Updated", "cap-new") with { SkipAmendWhenUnchanged = true }, ct: Ct);

        Assert.Empty(await RawRecordsAsync(repo));
    }

    [Fact]
    public async Task An_ordinary_capture_records_nothing()
    {
        var (repo, _) = await _ws.ThreePointsRepoAsync();
        _ws.Write(repo, "c.txt", "added later");

        await _ws.CaptureAsync(repo, "profile-1", "Fourth point");

        Assert.Empty(await RawRecordsAsync(repo));
    }

    [Fact]
    public async Task An_amend_stopped_before_it_rewrites_the_tip_has_lost_nothing_and_a_repeat_finishes_it()
    {
        var (repo, points) = await _ws.ThreePointsRepoAsync();
        _ws.Write(repo, "c.txt", "added later");
        _ws.OnInvoke = invocation => FailAt(invocation, candidate => candidate.Arguments[0] == "commit");
        var request = Amend("profile-1", "Updated", "cap-new");

        await Assert.ThrowsAsync<IOException>(() => _ws.Engine.CaptureAsync(repo, request, ct: Ct));

        Assert.Equal(points[2], await _ws.TipAsync(repo, "profile-1"));
        Assert.Single(await RawRecordsAsync(repo));
        Assert.True(Assert.Single(await _ws.Engine.ListRemovedAsync(repo, Ct)).OnBranch);

        _ws.OnInvoke = null;
        await _ws.Engine.CaptureAsync(repo, request, ct: Ct);

        Assert.Equal([points[2]], (await _ws.Engine.ListRemovedAsync(repo, Ct)).Select(entry => entry.Commit.Id));
    }

    [Fact]
    public async Task A_repository_with_no_records_lists_none_and_one_that_does_not_exist_lists_none()
    {
        var (repo, _) = await _ws.ThreePointsRepoAsync();

        Assert.Empty(await _ws.Engine.ListRemovedAsync(repo, Ct));
        Assert.Empty(await _ws.Engine.ListRemovedAsync(_ws.NewRepo(), Ct));
    }

    private static CaptureRequest Move(string branch, string start, string message, string captureId) =>
        new(branch, message, captureId) { Mode = CaptureMode.MoveForward, ExpectedStartCommit = start, CapturedAt = _at };

    private static CaptureRequest Amend(string branch, string message, string captureId) =>
        new(branch, message, captureId) { Mode = CaptureMode.Amend, CapturedAt = _at };

    // The write that keeps the dropped snapshots: one ref transaction read from stdin.
    private static bool IsRecordWrite(GitInvocation invocation) =>
        invocation.Arguments[0] == "update-ref" && invocation.Arguments.Contains("--stdin");

    private static bool IsBranchMove(GitInvocation invocation, string branch) =>
        invocation.Arguments[0] == "update-ref" && invocation.Arguments.Contains("refs/heads/" + branch);

    // Fails the git call asked for, as a full disk or a crash would, instead of running it.
    private static void FailAt(GitInvocation invocation, Func<GitInvocation, bool> isTheCall)
    {
        if (isTheCall(invocation))
        {
            throw new IOException("There is not enough space on the disk.");
        }
    }

    private static void StopNow(CancellationTokenSource cancel, bool byCancel)
    {
        if (byCancel)
        {
            cancel.Cancel();
        }
        else
        {
            throw new IOException("There is not enough space on the disk.");
        }
    }

    // The record refs as git holds them, whether or not any branch reaches their commit.
    private async Task<IReadOnlyList<string>> RawRecordsAsync(RepoRef repo) =>
        (await _ws.GitOutAsync(repo, "for-each-ref", "--format=%(refname)", "refs/savepoint/removed/")).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // profile-1 has three points; the folder was switched to the first and a new file was added while playing.
    private async Task<(RepoRef Repo, string[] Points)> PlayingFromTheFirstPointAsync()
    {
        var (repo, points) = await _ws.OlderPointRepoAsync();
        _ws.Write(repo, "played.txt", "progress made while playing");
        return (repo, points);
    }
}
