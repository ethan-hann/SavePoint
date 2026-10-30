using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// Reading a branch without asking git holds only for the files ref backend; a repository that keeps its refs in reftable
// storage has no ref files, and its branches still exist.
[Collection(EngineCollections.B)]
public sealed class ReftableBranchTests : IDisposable
{
    private readonly EngineWorkspace _ws = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    // A repository with one point and a second branch, moved to reftable storage.
    private async Task<RepoRef> ReftableRepoAsync()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");
        await _ws.CaptureAsync(repo, "profile-1");
        await _ws.Engine.CreateBranchAsync(repo, "spare", "profile-1", Ct);
        await _ws.GitWriteAsync(repo, "refs", "migrate", "--ref-format=reftable");
        Assert.True(Directory.Exists(Path.Combine(repo.GitDir, "reftable")));
        return repo;
    }

    [Fact]
    public async Task A_repository_in_reftable_storage_with_a_point_is_not_a_folder_nothing_was_saved_from()
    {
        var repo = await ReftableRepoAsync();

        Assert.False(await _ws.Engine.IsUnbornAsync(repo.GitDir, Ct));
    }

    [Fact]
    public async Task A_repository_in_reftable_storage_with_a_point_has_a_snapshot_to_look_for_stored_content_in()
    {
        var repo = await ReftableRepoAsync();

        Assert.True(GitEngine.HasSnapshot(repo));
    }

    [Fact]
    public async Task Deleting_a_branch_removes_it_from_a_repository_that_keeps_its_refs_in_reftable_storage()
    {
        var repo = await ReftableRepoAsync();

        await _ws.Engine.DeleteBranchAsync(repo, "spare", Ct);

        var remaining = (await _ws.GitAsync(repo, "for-each-ref", "--format=%(refname)", "refs/heads/")).StandardOutput;
        Assert.DoesNotContain("refs/heads/spare", remaining);
    }
}
