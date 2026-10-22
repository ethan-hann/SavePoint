using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// Free up space is the only thing that lets go of removed snapshots: it deletes the records first, then reclaims as it always did.
[Collection(EngineCollections.E)]
public sealed class RemovedSnapshotClearTests : IDisposable
{
    private const long _floor = 100L * 1024 * 1024;

    private readonly EngineWorkspace _ws = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    [Fact]
    public async Task Reclaiming_with_the_records_cleared_deletes_them_and_then_drops_what_no_branch_reaches()
    {
        var (repo, points) = await AfterAMoveForwardAsync();

        await _ws.Engine.RepackAsync(repo, OptimizationMode.ReclaimClearingRemoved, Ct);

        Assert.Empty(await RawRecordsAsync(repo));
        Assert.Empty(await _ws.Engine.ListRemovedAsync(repo, Ct));
        Assert.Equal(new[] { true, false, false }, await _ws.ObjectsExistAsync(repo, points[0], points[1], points[2]));
        Assert.Equal(2, (await _ws.Engine.ListCommitsAsync(repo, "profile-1", ct: Ct)).Count);
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
    public async Task A_free_up_the_volume_cannot_take_keeps_the_records()
    {
        var (repo, points) = await AfterAMoveForwardAsync();
        _ws.Space.Free = _floor / 2;

        var error = await Assert.ThrowsAsync<DiskSpaceException>(() => _ws.Engine.RepackAsync(repo, OptimizationMode.ReclaimClearingRemoved, Ct));

        Assert.Equal("error.diskSpace.low", error.MessageId);
        Assert.Equal(2, (await RawRecordsAsync(repo)).Count);
        Assert.Equal([points[2], points[1]], (await _ws.Engine.ListRemovedAsync(repo, Ct)).Select(entry => entry.Commit.Id));
    }

    [Fact]
    public async Task A_free_up_stopped_after_the_records_were_cleared_is_finished_by_repeating_it()
    {
        var (repo, points) = await AfterAMoveForwardAsync();
        _ws.OnInvoke = invocation =>
        {
            if (invocation.Arguments[0] == "reflog")
            {
                throw new IOException("The disk stopped answering.");
            }
        };

        await Assert.ThrowsAsync<IOException>(() => _ws.Engine.RepackAsync(repo, OptimizationMode.ReclaimClearingRemoved, Ct));
        Assert.Empty(await RawRecordsAsync(repo));

        _ws.OnInvoke = null;
        await _ws.Engine.RepackAsync(repo, OptimizationMode.ReclaimClearingRemoved, Ct);

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
