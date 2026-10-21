using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// Playing from an older point and then saving: keep it as a new profile (fork) or continue the profile from there (move forward).
[Collection(EngineCollections.D)]
public sealed class ForkAndMoveTests : IDisposable
{
    private readonly EngineWorkspace _ws = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    [Fact]
    public async Task Fork_from_an_older_point_makes_a_new_branch_there_and_leaves_the_old_one_and_every_file_alone()
    {
        var (repo, points) = await PlayingFromTheFirstPointAsync();
        var played = TreeSnapshot.Take(repo.WorkTree);

        var outcome = await _ws.Engine.CaptureAsync(repo, Fork("profile-fork", points[0], "Kept as new", "cap-fork"), ct: Ct);

        Assert.True(outcome.Changed);
        Assert.Equal(outcome.TipCommitId, await _ws.TipAsync(repo, "profile-fork"));
        Assert.Equal(points[0], await _ws.ResolveAsync(repo, "profile-fork~1"));
        Assert.Equal(2, await _ws.CountAsync(repo, "profile-fork"));
        Assert.Equal("refs/heads/profile-fork", await _ws.HeadRefAsync(repo));
        Assert.Equal(points[2], await _ws.TipAsync(repo, "profile-1"));
        Assert.Equal(3, await _ws.CountAsync(repo, "profile-1"));
        Assert.Equal(played, TreeSnapshot.Take(repo.WorkTree));
        Assert.Equal("Kept as new", await _ws.LogAsync(repo, "profile-fork", "%s"));
        Assert.Equal("cap-fork", await _ws.LogAsync(repo, "profile-fork", "%(trailers:key=Savepoint-Capture,valueonly)"));
        Assert.Equal(["a.txt", "played.txt"], (await _ws.TreeAsync(repo, "profile-fork")).Order());
        Assert.True((await _ws.Engine.GetStatusAsync(repo, Ct)).IsClean);
    }

    [Fact]
    public async Task Fork_refuses_when_the_folder_is_not_on_the_session_start_or_after_it()
    {
        var (repo, points) = await PlayingFromTheFirstPointAsync();
        var played = TreeSnapshot.Take(repo.WorkTree);

        var error = await Assert.ThrowsAsync<SessionMovedException>(
            () => _ws.Engine.CaptureAsync(repo, Fork("profile-fork", points[2], "x", "id"), ct: Ct));

        Assert.Equal("error.launch.sessionMoved", error.MessageId);
        Assert.Equal(RepoRole.Install, error.Args["role"]);
        Assert.Contains("started from", error.Message, StringComparison.Ordinal);
        Assert.Null(await _ws.TipAsync(repo, "profile-fork"));
        Assert.Null(await _ws.HeadRefAsync(repo));
        Assert.Equal(played, TreeSnapshot.Take(repo.WorkTree));
    }

    [Fact]
    public async Task Fork_accepts_a_folder_that_is_on_a_descendant_of_the_session_start()
    {
        var (repo, points) = await _ws.ThreePointsRepoAsync();
        await _ws.Engine.CheckoutCommitAsync(repo, points[2], ct: Ct);
        _ws.Write(repo, "played.txt", "x");

        var outcome = await _ws.Engine.CaptureAsync(repo, Fork("profile-fork", points[0], "Kept", "id"), ct: Ct);

        Assert.True(outcome.Changed);
        Assert.Equal(points[2], await _ws.ResolveAsync(repo, "profile-fork~1"));
    }

    [Fact]
    public async Task Fork_refuses_a_branch_name_that_is_taken()
    {
        var (repo, points) = await PlayingFromTheFirstPointAsync();

        var error = await Assert.ThrowsAsync<ProfileAlreadyExistsException>(
            () => _ws.Engine.CaptureAsync(repo, Fork("profile-1", points[0], "x", "id"), ct: Ct));

        Assert.Equal("error.profile.alreadyExists", error.MessageId);
        Assert.Equal("profile-1", error.Args["profileId"]);
        Assert.Equal(points[2], await _ws.TipAsync(repo, "profile-1"));
        Assert.Null(await _ws.HeadRefAsync(repo));
    }

    [Fact]
    public async Task Fork_without_changes_still_makes_the_branch_but_records_no_point()
    {
        var (repo, points) = await _ws.OlderPointRepoAsync();

        var outcome = await _ws.Engine.CaptureAsync(repo, Fork("profile-fork", points[0], "Kept", "id"), ct: Ct);

        Assert.False(outcome.Changed);
        Assert.Equal(points[0], outcome.TipCommitId);
        Assert.Equal(points[0], await _ws.TipAsync(repo, "profile-fork"));
        Assert.Equal("refs/heads/profile-fork", await _ws.HeadRefAsync(repo));
    }

    [Fact]
    public async Task Fork_from_the_branch_the_folder_is_on_starts_a_new_profile_from_the_current_state()
    {
        var (repo, points) = await _ws.ThreePointsRepoAsync();
        _ws.Write(repo, "extra.txt", "new profile content");

        var outcome = await _ws.Engine.CaptureAsync(repo, new CaptureRequest("profile-new", "Fresh", "cap-new") { Mode = CaptureMode.Fork }, ct: Ct);

        Assert.True(outcome.Changed);
        Assert.Equal(points[2], await _ws.ResolveAsync(repo, "profile-new~1"));
        Assert.Equal(points[2], await _ws.TipAsync(repo, "profile-1"));
        Assert.Equal("refs/heads/profile-new", await _ws.HeadRefAsync(repo));
    }

    [Fact]
    public async Task Fork_in_a_repository_with_no_point_yet_is_a_first_capture_on_the_new_branch()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");

        var outcome = await _ws.Engine.CaptureAsync(repo, new CaptureRequest("profile-new", "First", "id") { Mode = CaptureMode.Fork }, ct: Ct);

        Assert.True(outcome.Changed);
        Assert.Equal(1, await _ws.CountAsync(repo, "profile-new"));
        Assert.Equal("refs/heads/profile-new", await _ws.HeadRefAsync(repo));
    }

    [Fact]
    public async Task A_fork_stopped_between_making_the_branch_and_switching_to_it_can_be_repeated()
    {
        var (repo, points) = await PlayingFromTheFirstPointAsync();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        _ws.OnInvoke = invocation =>
        {
            if (invocation.Arguments[0] == "symbolic-ref")
            {
                cancel.Cancel();
            }
        };

        await Assert.ThrowsAsync<OperationCancelledByUserException>(
            () => _ws.Engine.CaptureAsync(repo, Fork("profile-fork", points[0], "Kept", "id"), ct: cancel.Token));
        Assert.Equal(points[0], await _ws.TipAsync(repo, "profile-fork"));
        Assert.Null(await _ws.HeadRefAsync(repo));

        _ws.OnInvoke = null;
        var outcome = await _ws.Engine.CaptureAsync(repo, Fork("profile-fork", points[0], "Kept", "id"), ct: Ct);

        Assert.True(outcome.Changed);
        Assert.Equal("refs/heads/profile-fork", await _ws.HeadRefAsync(repo));
        Assert.Equal(points[0], await _ws.ResolveAsync(repo, "profile-fork~1"));
    }

    [Fact]
    public async Task Move_forward_points_the_branch_at_the_older_point_and_records_the_new_one_on_it()
    {
        var (repo, points) = await PlayingFromTheFirstPointAsync();
        var played = TreeSnapshot.Take(repo.WorkTree);

        var outcome = await _ws.Engine.CaptureAsync(repo, Move("profile-1", points[0], "Continued", "cap-move"), ct: Ct);

        Assert.True(outcome.Changed);
        Assert.Equal(outcome.TipCommitId, await _ws.TipAsync(repo, "profile-1"));
        Assert.Equal(points[0], await _ws.ResolveAsync(repo, "profile-1~1"));
        Assert.Equal(2, await _ws.CountAsync(repo, "profile-1"));
        Assert.Equal("refs/heads/profile-1", await _ws.HeadRefAsync(repo));
        Assert.Equal(played, TreeSnapshot.Take(repo.WorkTree));
        Assert.Equal("cap-move", await _ws.LogAsync(repo, "profile-1", "%(trailers:key=Savepoint-Capture,valueonly)"));
        Assert.True((await _ws.Engine.GetStatusAsync(repo, Ct)).IsClean);
    }

    [Fact]
    public async Task The_points_a_move_forward_drops_are_still_in_the_store_until_a_reclaim()
    {
        var (repo, points) = await PlayingFromTheFirstPointAsync();

        await _ws.Engine.CaptureAsync(repo, Move("profile-1", points[0], "Continued", "cap-move"), ct: Ct);

        Assert.Equal("commit", await _ws.GitOutAsync(repo, "cat-file", "-t", points[2]));
        Assert.DoesNotContain(points[2], (await _ws.Engine.ListCommitsAsync(repo, "profile-1", ct: Ct)).Select(commit => commit.Id));
    }

    [Fact]
    public async Task Move_forward_refuses_when_the_folder_is_not_on_the_session_start_or_after_it()
    {
        var (repo, points) = await PlayingFromTheFirstPointAsync();

        await Assert.ThrowsAsync<SessionMovedException>(() => _ws.Engine.CaptureAsync(repo, Move("profile-1", points[2], "x", "id"), ct: Ct));

        Assert.Equal(points[2], await _ws.TipAsync(repo, "profile-1"));
        Assert.Null(await _ws.HeadRefAsync(repo));
    }

    [Fact]
    public async Task Move_forward_needs_to_know_where_the_session_started()
    {
        var (repo, _) = await PlayingFromTheFirstPointAsync();

        await Assert.ThrowsAsync<ArgumentException>(
            () => _ws.Engine.CaptureAsync(repo, new CaptureRequest("profile-1", "x", "id") { Mode = CaptureMode.MoveForward }, ct: Ct));
    }

    [Fact]
    public async Task Move_forward_of_a_branch_that_does_not_exist_is_refused()
    {
        var (repo, points) = await PlayingFromTheFirstPointAsync();

        var error = await Assert.ThrowsAsync<ProfileHasNoPointsException>(() => _ws.Engine.CaptureAsync(repo, Move("ghost", points[0], "x", "id"), ct: Ct));

        Assert.Equal("error.profile.noPoints", error.MessageId);
        Assert.Equal("ghost", error.Args["profileId"]);
    }

    [Fact]
    public async Task Move_forward_from_a_folder_that_is_on_no_point_is_refused_as_a_moved_session()
    {
        var repo = await _ws.InitializedRepoAsync();
        await _ws.Engine.CreateEmptyBranchAsync(repo, "profile-1", ct: Ct);
        var start = (await _ws.TipAsync(repo, "profile-1"))!;

        var error = await Assert.ThrowsAsync<SessionMovedException>(() => _ws.Engine.CaptureAsync(repo, Move("profile-1", start, "x", "id"), ct: Ct));

        Assert.Equal(RepoRole.Install, error.Args["role"]);
    }

    [Fact]
    public async Task Move_forward_refuses_a_point_that_is_not_in_the_branch_s_history()
    {
        var (repo, points) = await PlayingFromTheFirstPointAsync();
        await _ws.Engine.CreateEmptyBranchAsync(repo, "elsewhere", ct: Ct);
        var unrelated = (await _ws.TipAsync(repo, "elsewhere"))!;
        await _ws.GitWriteAsync(repo, "checkout", "--quiet", "--detach", unrelated);
        _ws.Write(repo, "x.txt", "x");

        await Assert.ThrowsAsync<SessionMovedException>(() => _ws.Engine.CaptureAsync(repo, Move("profile-1", unrelated, "x", "id"), ct: Ct));

        Assert.Equal(points[2], await _ws.TipAsync(repo, "profile-1"));
    }

    [Fact]
    public async Task Move_forward_without_changes_still_moves_the_branch()
    {
        var (repo, points) = await _ws.OlderPointRepoAsync();

        var outcome = await _ws.Engine.CaptureAsync(repo, Move("profile-1", points[0], "Continued", "id"), ct: Ct);

        Assert.False(outcome.Changed);
        Assert.Equal(points[0], outcome.TipCommitId);
        Assert.Equal(points[0], await _ws.TipAsync(repo, "profile-1"));
        Assert.Equal("refs/heads/profile-1", await _ws.HeadRefAsync(repo));
    }

    [Fact]
    public async Task A_move_forward_stopped_after_moving_the_branch_can_be_repeated()
    {
        var (repo, points) = await PlayingFromTheFirstPointAsync();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        _ws.OnInvoke = invocation =>
        {
            if (invocation.Arguments[0] == "symbolic-ref")
            {
                cancel.Cancel();
            }
        };

        await Assert.ThrowsAsync<OperationCancelledByUserException>(
            () => _ws.Engine.CaptureAsync(repo, Move("profile-1", points[0], "Continued", "id"), ct: cancel.Token));
        Assert.Equal(points[0], await _ws.TipAsync(repo, "profile-1"));

        _ws.OnInvoke = null;
        var outcome = await _ws.Engine.CaptureAsync(repo, Move("profile-1", points[0], "Continued", "id"), ct: Ct);

        Assert.True(outcome.Changed);
        Assert.Equal(points[0], await _ws.ResolveAsync(repo, "profile-1~1"));
        Assert.Equal("refs/heads/profile-1", await _ws.HeadRefAsync(repo));
    }

    [Fact]
    public async Task Move_forward_on_the_branch_already_checked_out_is_an_ordinary_capture()
    {
        var (repo, points) = await _ws.ThreePointsRepoAsync();
        _ws.Write(repo, "c.txt", "c");

        var outcome = await _ws.Engine.CaptureAsync(repo, Move("profile-1", points[2], "Next", "id"), ct: Ct);

        Assert.True(outcome.Changed);
        Assert.Equal(points[2], await _ws.ResolveAsync(repo, "profile-1~1"));
        Assert.Equal(4, await _ws.CountAsync(repo, "profile-1"));
    }

    private static CaptureRequest Fork(string branch, string start, string message, string captureId) =>
        new(branch, message, captureId) { Mode = CaptureMode.Fork, ExpectedStartCommit = start };

    private static CaptureRequest Move(string branch, string start, string message, string captureId) =>
        new(branch, message, captureId) { Mode = CaptureMode.MoveForward, ExpectedStartCommit = start };

    // profile-1 has three points; the folder was switched to the first and a new file was added while playing.
    private async Task<(RepoRef Repo, string[] Points)> PlayingFromTheFirstPointAsync()
    {
        var (repo, points) = await _ws.OlderPointRepoAsync();
        _ws.Write(repo, "played.txt", "progress made while playing");
        return (repo, points);
    }
}
