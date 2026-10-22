using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// A profile's first save of a folder that other profiles have saved starts a history of its own instead of branching from theirs.
[Collection(EngineCollections.D)]
public sealed class CaptureRootTests : IDisposable
{
    private readonly EngineWorkspace _ws = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    [Fact]
    public async Task Root_starts_a_branch_with_one_point_that_has_no_parent_and_holds_what_the_folder_holds()
    {
        var (repo, points) = await _ws.ThreePointsRepoAsync();
        _ws.Write(repo, "extra.txt", "new profile content");

        var outcome = await _ws.Engine.CaptureAsync(repo, Root("profile-new", "Fresh", "cap-new"), ct: Ct);

        Assert.True(outcome.Changed);
        Assert.Equal(outcome.TipCommitId, await _ws.TipAsync(repo, "profile-new"));
        Assert.Equal(1, await _ws.CountAsync(repo, "profile-new"));
        Assert.Equal(string.Empty, await _ws.LogAsync(repo, "profile-new", "%P"));
        Assert.Equal(["a.txt", "b.txt", "extra.txt"], (await _ws.TreeAsync(repo, "profile-new")).Order());
        Assert.Equal("cap-new", await _ws.LogAsync(repo, "profile-new", "%(trailers:key=Savepoint-Capture,valueonly)"));
        Assert.Equal(points[2], await _ws.TipAsync(repo, "profile-1"));
        Assert.Equal(3, await _ws.CountAsync(repo, "profile-1"));
        Assert.Equal("refs/heads/profile-new", await _ws.HeadRefAsync(repo));
        Assert.True((await _ws.Engine.GetStatusAsync(repo, Ct)).IsClean);
    }

    [Fact]
    public async Task Root_writes_a_point_even_when_the_folder_is_the_same_as_the_branch_it_is_on()
    {
        var (repo, points) = await _ws.ThreePointsRepoAsync();

        var outcome = await _ws.Engine.CaptureAsync(repo, Root("profile-new", "Fresh", "cap-new"), ct: Ct);

        Assert.True(outcome.Changed);
        Assert.NotEqual(points[2], outcome.TipCommitId);
        Assert.Equal(1, await _ws.CountAsync(repo, "profile-new"));
        Assert.Equal(string.Empty, await _ws.LogAsync(repo, "profile-new", "%P"));
    }

    [Fact]
    public async Task Root_in_a_repository_with_no_point_yet_is_a_first_capture_on_the_new_branch()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");

        var outcome = await _ws.Engine.CaptureAsync(repo, Root("profile-new", "First", "id"), ct: Ct);

        Assert.True(outcome.Changed);
        Assert.Equal(1, await _ws.CountAsync(repo, "profile-new"));
        Assert.Equal("refs/heads/profile-new", await _ws.HeadRefAsync(repo));
    }

    [Fact]
    public async Task Root_refuses_a_branch_that_holds_other_history_and_moves_nothing()
    {
        var (repo, points) = await _ws.ThreePointsRepoAsync();

        var error = await Assert.ThrowsAsync<ProfileAlreadyExistsException>(() => _ws.Engine.CaptureAsync(repo, Root("profile-1", "x", "id"), ct: Ct));

        Assert.Equal("profile-1", error.Args["profileId"]);
        Assert.Equal(points[2], await _ws.TipAsync(repo, "profile-1"));
        Assert.Equal("refs/heads/profile-1", await _ws.HeadRefAsync(repo));
    }

    [Fact]
    public async Task A_root_stopped_after_the_branch_was_made_and_before_head_followed_is_finished_by_repeating_it()
    {
        var (repo, points) = await _ws.ThreePointsRepoAsync();
        _ws.OnInvoke = invocation => FailAt(invocation, "symbolic-ref");

        await Assert.ThrowsAsync<IOException>(() => _ws.Engine.CaptureAsync(repo, Root("profile-new", "Fresh", "id"), ct: Ct));
        var recorded = await _ws.TipAsync(repo, "profile-new");
        Assert.Equal("refs/heads/profile-1", await _ws.HeadRefAsync(repo));

        _ws.OnInvoke = null;
        var outcome = await _ws.Engine.CaptureAsync(repo, Root("profile-new", "Fresh", "id"), ct: Ct);

        Assert.True(outcome.Changed);
        Assert.Equal(recorded, outcome.TipCommitId);
        Assert.Equal(1, await _ws.CountAsync(repo, "profile-new"));
        Assert.Equal("refs/heads/profile-new", await _ws.HeadRefAsync(repo));
        Assert.Equal(points[2], await _ws.TipAsync(repo, "profile-1"));
        Assert.True((await _ws.Engine.GetStatusAsync(repo, Ct)).IsClean);
    }

    [Fact]
    public async Task A_root_repeated_under_another_capture_id_does_not_take_over_the_branch_an_earlier_one_made()
    {
        var (repo, _) = await _ws.ThreePointsRepoAsync();
        _ws.OnInvoke = invocation => FailAt(invocation, "symbolic-ref");
        await Assert.ThrowsAsync<IOException>(() => _ws.Engine.CaptureAsync(repo, Root("profile-new", "Fresh", "first-try"), ct: Ct));
        _ws.OnInvoke = null;

        await Assert.ThrowsAsync<ProfileAlreadyExistsException>(() => _ws.Engine.CaptureAsync(repo, Root("profile-new", "Fresh", "another-capture"), ct: Ct));

        Assert.Equal("refs/heads/profile-1", await _ws.HeadRefAsync(repo));
    }

    private static CaptureRequest Root(string branch, string message, string captureId) =>
        new(branch, message, captureId) { Mode = CaptureMode.Root };

    private static void FailAt(GitInvocation invocation, string command)
    {
        if (invocation.Arguments[0] == command)
        {
            throw new IOException("There is not enough space on the disk.");
        }
    }
}
