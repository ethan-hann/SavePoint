using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// Free up space is the only thing that lets go of removed snapshots: it deletes the records it was given, then reclaims as it always did.
[Collection(EngineCollections.E)]
public sealed class RemovedSnapshotClearTests : IDisposable
{
    private const long _floor = 100L * 1024 * 1024;

    private readonly EngineWorkspace _ws = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    [Fact]
    public async Task Clearing_the_records_and_reclaiming_drops_what_no_branch_reaches()
    {
        var (repo, points) = await AfterAMoveForwardAsync();

        var cleared = await _ws.Engine.ClearRemovedAsync(repo, await RawRecordsAsync(repo), Ct);
        await _ws.Engine.RepackAsync(repo, OptimizationMode.Reclaim, Ct);

        Assert.Equal(2, cleared.Count);
        Assert.Empty(await RawRecordsAsync(repo));
        Assert.Empty(await _ws.Engine.ListRemovedAsync(repo, Ct));
        Assert.Equal(new[] { true, false, false }, await _ws.ObjectsExistAsync(repo, points[0], points[1], points[2]));
        Assert.Equal(2, (await _ws.Engine.ListCommitsAsync(repo, "profile-1", ct: Ct)).Count);
    }

    [Fact]
    public async Task Clearing_deletes_only_the_records_it_is_given_and_the_reclaim_keeps_what_the_others_reach()
    {
        var (repo, points) = await AfterAMoveForwardAsync();
        var listed = await _ws.Engine.ListRemovedAsync(repo, Ct);
        var newest = listed.Single(entry => entry.Commit.Id == points[2]);

        var cleared = await _ws.Engine.ClearRemovedAsync(repo, [newest.RefName], Ct);
        await _ws.Engine.RepackAsync(repo, OptimizationMode.Reclaim, Ct);

        // The second point is still recorded, so it survives; the third had only its own record, so it goes.
        Assert.Equal([newest.RefName], cleared);
        Assert.Equal([points[1]], (await _ws.Engine.ListRemovedAsync(repo, Ct)).Select(entry => entry.Commit.Id));
        Assert.Equal(new[] { true, false }, await _ws.ObjectsExistAsync(repo, points[1], points[2]));
    }

    [Fact]
    public async Task Clearing_skips_a_name_that_is_no_record_and_a_record_that_is_already_gone()
    {
        var (repo, points) = await AfterAMoveForwardAsync();
        var raw = await RawRecordsAsync(repo);

        var first = await _ws.Engine.ClearRemovedAsync(repo, [.. raw, "refs/heads/profile-1", "refs/savepoint/removed/not-a-record"], Ct);
        var again = await _ws.Engine.ClearRemovedAsync(repo, raw, Ct);

        Assert.Equal(2, first.Count);
        Assert.Empty(again);
        Assert.Equal(points[0], (await _ws.Engine.ListCommitsAsync(repo, "profile-1", ct: Ct)).Last().Id);
        Assert.Equal(["profile-1"], await _ws.Engine.ListBranchesAsync(repo, Ct));
    }

    [Fact]
    public async Task Cleared_records_can_be_put_back_before_a_reclaim_and_then_keep_their_snapshots_alive()
    {
        var (repo, points) = await AfterAMoveForwardAsync();
        var before = await _ws.Engine.ListRemovedAsync(repo, Ct);
        var cleared = await _ws.Engine.ClearRemovedAsync(repo, await RawRecordsAsync(repo), Ct);

        await _ws.Engine.RestoreRemovedAsync(repo, [.. cleared, "refs/heads/profile-1", "refs/savepoint/removed/not-a-record"], Ct);
        await _ws.Engine.RepackAsync(repo, OptimizationMode.Reclaim, Ct);

        Assert.Equal(before.Select(entry => (entry.RefName, entry.Commit.Id)), (await _ws.Engine.ListRemovedAsync(repo, Ct)).Select(entry => (entry.RefName, entry.Commit.Id)));
        Assert.Equal(new[] { true, true }, await _ws.ObjectsExistAsync(repo, points[1], points[2]));
        Assert.Equal(["profile-1"], await _ws.Engine.ListBranchesAsync(repo, Ct));
    }

    [Fact]
    public async Task A_plain_reclaim_and_a_routine_run_leave_the_records_alone()
    {
        var (repo, points) = await AfterAMoveForwardAsync();

        await _ws.Engine.RepackAsync(repo, OptimizationMode.Routine, Ct);
        await _ws.Engine.RepackAsync(repo, OptimizationMode.Reclaim, Ct);

        Assert.Equal(2, (await RawRecordsAsync(repo)).Count);
        Assert.Equal(new[] { true, true }, await _ws.ObjectsExistAsync(repo, points[1], points[2]));
    }

    [Fact]
    public async Task A_check_the_volume_cannot_pass_refuses_the_reclaim_and_leaves_the_records_alone()
    {
        var (repo, points) = await AfterAMoveForwardAsync();
        _ws.Space.Free = _floor / 2;

        var error = await Assert.ThrowsAsync<DiskSpaceException>(() => _ws.Engine.CheckReclaimAsync(repo, Ct));

        Assert.Equal("error.diskSpace.low", error.MessageId);
        Assert.Equal(2, (await RawRecordsAsync(repo)).Count);
        Assert.Equal([points[2], points[1]], (await _ws.Engine.ListRemovedAsync(repo, Ct)).Select(entry => entry.Commit.Id));
    }

    [Fact]
    public async Task A_reclaim_stopped_after_the_records_were_cleared_is_finished_by_repeating_it()
    {
        var (repo, points) = await AfterAMoveForwardAsync();
        _ws.OnInvoke = invocation =>
        {
            if (invocation.Arguments[0] == "reflog")
            {
                throw new IOException("The disk stopped answering.");
            }
        };

        await _ws.Engine.ClearRemovedAsync(repo, await RawRecordsAsync(repo), Ct);
        await Assert.ThrowsAsync<IOException>(() => _ws.Engine.RepackAsync(repo, OptimizationMode.Reclaim, Ct));
        Assert.Empty(await RawRecordsAsync(repo));

        _ws.OnInvoke = null;
        await _ws.Engine.RepackAsync(repo, OptimizationMode.Reclaim, Ct);

        Assert.Equal(new[] { true, false, false }, await _ws.ObjectsExistAsync(repo, points[0], points[1], points[2]));
        Assert.Equal(2, (await _ws.Engine.ListCommitsAsync(repo, "profile-1", ct: Ct)).Count);
    }

    // profile-1 had three points; the folder was switched to the first and the profile continued from there, which dropped the other two.
    private async Task<(RepoRef Repo, string[] Points)> AfterAMoveForwardAsync()
    {
        var (repo, points) = await _ws.OlderPointRepoAsync();
        _ws.Write(repo, "played.txt", "progress made while playing");
        await _ws.Engine.CaptureAsync(repo, new CaptureRequest("profile-1", "Continued", "cap-move") { Mode = CaptureMode.MoveForward, ExpectedStartCommit = points[0] }, ct: Ct);
        return (repo, points);
    }

    private async Task<IReadOnlyList<string>> RawRecordsAsync(RepoRef repo) =>
        (await _ws.GitOutAsync(repo, "for-each-ref", "--format=%(refname)", "refs/savepoint/removed/")).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
