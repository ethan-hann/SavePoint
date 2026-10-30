namespace Savepoint.Core.Tests.Git.Engine;

// Reading a branch without asking git holds only for the files ref backend; a repository that keeps its refs in reftable
// storage has no ref files, and its branches still exist.
[Collection(EngineCollections.B)]
public sealed class ReftableBranchTests : IDisposable
{
    private readonly EngineWorkspace _ws = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    [Fact]
    public async Task Deleting_a_branch_removes_it_from_a_repository_that_keeps_its_refs_in_reftable_storage()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");
        await _ws.CaptureAsync(repo, "profile-1");
        await _ws.Engine.CreateBranchAsync(repo, "spare", "profile-1", Ct);
        await _ws.GitWriteAsync(repo, "refs", "migrate", "--ref-format=reftable");
        Assert.True(Directory.Exists(Path.Combine(repo.GitDir, "reftable")));

        await _ws.Engine.DeleteBranchAsync(repo, "spare", Ct);

        var remaining = (await _ws.GitAsync(repo, "for-each-ref", "--format=%(refname)", "refs/heads/")).StandardOutput;
        Assert.DoesNotContain("refs/heads/spare", remaining);
    }
}
