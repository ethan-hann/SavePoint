namespace Savepoint.Core.Tests.Git.Engine;

// What deleting a profile would lose is what no other profile reaches, however many other profiles there are.
[Collection(EngineCollections.B)]
public sealed class UniquePointsTests : IDisposable
{
    private readonly EngineWorkspace _ws = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    [Fact]
    public async Task Every_other_profile_counts_not_only_the_first_and_a_duplicate_changes_nothing()
    {
        var (repo, points) = await _ws.ThreePointsRepoAsync();
        await _ws.Engine.CreateBranchAsync(repo, "x", points[1], Ct);
        await _ws.Engine.CreateBranchAsync(repo, "y", points[0], Ct);
        await _ws.Engine.CreateBranchAsync(repo, "z", points[2], Ct);
        await _ws.Engine.CreateBranchAsync(repo, "w", points[2], Ct);
        await _ws.Engine.CheckoutBranchAsync(repo, "x", ct: Ct);
        _ws.Write(repo, "only-x.txt", "x");
        var onlyX = (await _ws.CaptureAsync(repo, "x", "Only here", null)).TipCommitId!;

        var unique = await _ws.Engine.ListCommitsOnlyOnAsync(repo, "x", ["y", "z", "w"], Ct);

        Assert.Equal([onlyX], unique.Select(commit => commit.Id));
    }

    [Fact]
    public async Task A_point_the_first_of_two_other_profiles_reaches_is_not_lost_though_the_second_does_not_reach_it()
    {
        var (repo, points) = await _ws.ThreePointsRepoAsync();
        await _ws.Engine.CreateBranchAsync(repo, "x", points[2], Ct);
        await _ws.Engine.CreateBranchAsync(repo, "y", points[1], Ct);
        await _ws.Engine.CreateBranchAsync(repo, "z", points[0], Ct);

        var unique = await _ws.Engine.ListCommitsOnlyOnAsync(repo, "x", ["y", "z"], Ct);

        Assert.Equal([points[2]], unique.Select(commit => commit.Id));
    }
}
