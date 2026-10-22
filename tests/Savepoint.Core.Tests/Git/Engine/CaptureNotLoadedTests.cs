using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// A capture records what the folder holds now, so it can only add to the branch the folder is on. Any other branch is refused with its own error, before anything is staged.
[Collection(EngineCollections.E)]
public sealed class CaptureNotLoadedTests : IDisposable
{
    private readonly EngineWorkspace _ws = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    [Fact]
    public async Task A_capture_onto_a_branch_while_another_is_checked_out_is_refused_with_the_not_loaded_error_and_writes_nothing()
    {
        var (repo, points) = await _ws.ThreePointsRepoAsync();
        await _ws.Engine.CreateBranchAsync(repo, "profile-2", points[1], Ct);
        _ws.Write(repo, "new.txt", "what profile-1 would have put into profile-2");
        var from = _ws.Invocations.Count;

        var error = await Assert.ThrowsAsync<ProfileNotLoadedException>(() => _ws.Engine.CaptureAsync(repo, new CaptureRequest("profile-2", "Not mine", "id"), ct: Ct));

        Assert.Equal("error.profile.notLoaded", error.MessageId);
        Assert.Equal("profile-2", error.Args["profileId"]);
        Assert.Equal(RepoRole.Install, error.Args["role"]);
        Assert.Equal(points[1], await _ws.TipAsync(repo, "profile-2"));
        Assert.Equal(points[2], await _ws.TipAsync(repo, "profile-1"));
        Assert.Equal("refs/heads/profile-1", await _ws.HeadRefAsync(repo));
        Assert.DoesNotContain(_ws.Invocations.Skip(from), invocation => invocation.Arguments[0] is "add" or "commit" or "commit-tree" or "update-ref");
    }
}
