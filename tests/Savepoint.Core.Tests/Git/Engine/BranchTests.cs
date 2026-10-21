using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

[Collection(EngineCollections.E)]
public sealed class BranchTests : IDisposable
{
    private readonly EngineWorkspace _ws = new(options => options with { LockRetryDelays = [TimeSpan.FromMilliseconds(400)] });

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    [Fact]
    public async Task A_branch_can_start_at_a_commit_and_nothing_else_moves()
    {
        var repo = await TwoPointRepoAsync();
        var first = await _ws.GitOutAsync(repo, "rev-parse", "profile-1~1");
        _ws.Write(repo, "a.txt", "edited but not captured");

        await _ws.Engine.CreateBranchAsync(repo, "profile-2", first, Ct);

        Assert.Equal(first, await _ws.TipAsync(repo, "profile-2"));
        Assert.Equal("refs/heads/profile-1", await _ws.HeadRefAsync(repo));
        Assert.Equal("edited but not captured", await File.ReadAllTextAsync(_ws.FileOf(repo, "a.txt"), Ct));
    }

    [Fact]
    public async Task A_branch_can_start_at_another_branch()
    {
        var repo = await TwoPointRepoAsync();

        await _ws.Engine.CreateBranchAsync(repo, "copy", "profile-1", Ct);

        Assert.Equal(await _ws.TipAsync(repo, "profile-1"), await _ws.TipAsync(repo, "copy"));
    }

    [Fact]
    public async Task Creating_a_branch_that_exists_fails_at_once_without_retrying_and_changes_nothing()
    {
        var repo = await TwoPointRepoAsync();
        var tip = await _ws.TipAsync(repo, "profile-1");
        var older = await _ws.GitOutAsync(repo, "rev-parse", "profile-1~1");
        var before = _ws.Invocations.Count(invocation => invocation.Arguments[0] == "update-ref");

        var error = await Assert.ThrowsAsync<GitOperationException>(() => _ws.Engine.CreateBranchAsync(repo, "profile-1", older, Ct));

        Assert.Contains("already exists", error.Message, StringComparison.Ordinal);
        Assert.Equal(tip, await _ws.TipAsync(repo, "profile-1"));
        Assert.Equal(before + 1, _ws.Invocations.Count(invocation => invocation.Arguments[0] == "update-ref"));
    }

    [Theory]
    [InlineData("ghost")]
    [InlineData("1111111111111111111111111111111111111111")]
    public async Task Creating_a_branch_at_a_start_point_that_does_not_exist_fails(string startPoint)
    {
        var repo = await TwoPointRepoAsync();

        await Assert.ThrowsAsync<GitOperationException>(() => _ws.Engine.CreateBranchAsync(repo, "new", startPoint, Ct));

        Assert.Null(await _ws.TipAsync(repo, "new"));
    }

    [Fact]
    public async Task Creating_a_branch_with_a_bad_name_is_rejected()
    {
        var repo = await TwoPointRepoAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => _ws.Engine.CreateBranchAsync(repo, "a..b", "profile-1", Ct));
    }

    [Fact]
    public async Task An_empty_branch_has_one_point_with_an_empty_tree_and_head_and_files_stay()
    {
        var repo = await TwoPointRepoAsync();
        _ws.Write(repo, "untouched.txt", "still here");

        await _ws.Engine.CreateEmptyBranchAsync(repo, "fresh", ct: Ct);

        Assert.Equal(1, await _ws.CountAsync(repo, "fresh"));
        Assert.Empty(await _ws.TreeAsync(repo, "fresh"));
        Assert.Equal("Profile started", await _ws.LogAsync(repo, "fresh", "%s"));
        Assert.Equal("refs/heads/profile-1", await _ws.HeadRefAsync(repo));
        Assert.Equal("still here", await File.ReadAllTextAsync(_ws.FileOf(repo, "untouched.txt"), Ct));
        Assert.Equal("one", await File.ReadAllTextAsync(_ws.FileOf(repo, "a.txt"), Ct));
    }

    [Fact]
    public async Task An_empty_branch_records_the_message_and_capture_id_it_was_given()
    {
        var repo = await TwoPointRepoAsync();

        await _ws.Engine.CreateEmptyBranchAsync(repo, "fresh", "Brand new saves", "cap-77", Ct);

        Assert.Equal("Brand new saves", await _ws.LogAsync(repo, "fresh", "%s"));
        Assert.Equal("cap-77", await _ws.LogAsync(repo, "fresh", "%(trailers:key=Savepoint-Capture,valueonly)"));
    }

    [Fact]
    public async Task Making_an_empty_branch_twice_changes_nothing_the_second_time()
    {
        var repo = await TwoPointRepoAsync();
        await _ws.Engine.CreateEmptyBranchAsync(repo, "fresh", ct: Ct);
        var tip = await _ws.TipAsync(repo, "fresh");

        await _ws.Engine.CreateEmptyBranchAsync(repo, "fresh", "Other message", "cap-1", Ct);

        Assert.Equal(tip, await _ws.TipAsync(repo, "fresh"));
    }

    [Fact]
    public async Task An_empty_branch_works_in_a_repository_that_has_no_point_yet()
    {
        var repo = await _ws.InitializedRepoAsync();

        await _ws.Engine.CreateEmptyBranchAsync(repo, "fresh", ct: Ct);

        Assert.True(await _ws.Engine.BranchExistsAsync(repo, "fresh", Ct));
        Assert.Equal(1, await _ws.CountAsync(repo, "fresh"));
    }

    [Fact]
    public async Task The_empty_tree_is_a_real_object_so_a_pack_never_misses_it()
    {
        var repo = await TwoPointRepoAsync();

        await _ws.Engine.CreateEmptyBranchAsync(repo, "fresh", ct: Ct);

        var kind = await _ws.GitOutAsync(repo, "cat-file", "-t", "4b825dc642cb6eb9a060e54bf8d69288fbee4904");
        Assert.Equal("tree", kind);
        var loose = Path.Combine(repo.GitDir, "objects", "4b", "825dc642cb6eb9a060e54bf8d69288fbee4904");
        Assert.True(File.Exists(loose), "the empty tree object should be stored, not only known to git");
    }

    [Fact]
    public async Task A_branch_that_is_not_checked_out_can_be_deleted()
    {
        var repo = await TwoPointRepoAsync();
        await _ws.Engine.CreateBranchAsync(repo, "spare", "profile-1", Ct);

        await _ws.Engine.DeleteBranchAsync(repo, "spare", Ct);

        Assert.Null(await _ws.TipAsync(repo, "spare"));
        Assert.NotNull(await _ws.TipAsync(repo, "profile-1"));
    }

    [Fact]
    public async Task Deleting_a_branch_that_is_already_gone_does_nothing()
    {
        var repo = await TwoPointRepoAsync();

        await _ws.Engine.DeleteBranchAsync(repo, "never-existed", Ct);
        await _ws.Engine.DeleteBranchAsync(repo, "never-existed", Ct);

        Assert.Equal(["profile-1"], await _ws.Engine.ListBranchesAsync(repo, Ct));
    }

    [Fact]
    public async Task The_checked_out_branch_cannot_be_deleted()
    {
        var repo = await TwoPointRepoAsync();

        var error = await Assert.ThrowsAsync<ProfileActiveException>(() => _ws.Engine.DeleteBranchAsync(repo, "profile-1", Ct));

        Assert.Equal("error.gameService.profileActive", error.MessageId);
        Assert.Equal("profile-1", error.Args["profileId"]);
        Assert.NotNull(await _ws.TipAsync(repo, "profile-1"));
    }

    [Fact]
    public async Task Deleting_a_branch_after_the_whole_repository_is_gone_does_nothing()
    {
        var repo = await TwoPointRepoAsync();
        GitWorkspace.DeleteDirectory(repo.GitDir);

        await _ws.Engine.DeleteBranchAsync(repo, "profile-1", Ct);

        Assert.False(Directory.Exists(repo.GitDir));
    }

    [Fact]
    public async Task Deleting_a_branch_still_refuses_another_games_folder()
    {
        var repo = await TwoPointRepoAsync();
        var intruder = EngineWorkspace.Claim(repo, "other", RepoRole.External);
        await _ws.GitWriteAsync(repo, "branch", "spare", "profile-1");

        await Assert.ThrowsAsync<ConsentMarkerException>(() => _ws.Engine.DeleteBranchAsync(intruder, "spare", Ct));

        Assert.NotNull(await _ws.TipAsync(repo, "spare"));
    }

    [Fact]
    public async Task Branch_writes_refuse_a_folder_without_a_marker_even_for_a_branch_that_is_gone()
    {
        var repo = await TwoPointRepoAsync();
        File.Delete(EngineWorkspace.MarkerPath(repo));

        await Assert.ThrowsAsync<ConsentMarkerException>(() => _ws.Engine.DeleteBranchAsync(repo, "never-existed", Ct));
        await Assert.ThrowsAsync<ConsentMarkerException>(() => _ws.Engine.CreateBranchAsync(repo, "x", "profile-1", Ct));
        await Assert.ThrowsAsync<ConsentMarkerException>(() => _ws.Engine.CreateEmptyBranchAsync(repo, "x", ct: Ct));
    }

    [Fact]
    public async Task Branch_writes_refuse_a_protected_work_tree()
    {
        var repo = await TwoPointRepoAsync();
        var protectedRepo = repo with { WorkTree = _ws.Platform.Directories.HomeDir };
        Directory.CreateDirectory(protectedRepo.WorkTree);

        await Assert.ThrowsAsync<PathSafetyException>(() => _ws.Engine.DeleteBranchAsync(protectedRepo, "x", Ct));
        await Assert.ThrowsAsync<PathSafetyException>(() => _ws.Engine.CreateBranchAsync(protectedRepo, "x", "profile-1", Ct));
        await Assert.ThrowsAsync<PathSafetyException>(() => _ws.Engine.CreateEmptyBranchAsync(protectedRepo, "x", ct: Ct));
    }

    [Fact]
    public async Task History_can_still_be_read_after_the_game_folder_has_moved_away()
    {
        var repo = await TwoPointRepoAsync();
        var moved = repo.WorkTree + "-moved";
        Directory.Move(repo.WorkTree, moved);

        var commits = await _ws.Engine.ListCommitsAsync(repo, "profile-1", ct: Ct);

        Assert.Equal(2, commits.Count);
        Assert.Equal(["profile-1"], await _ws.Engine.ListBranchesAsync(repo, Ct));
    }

    private async Task<RepoRef> TwoPointRepoAsync() => (await _ws.TwoPointsRepoAsync()).Repo;
}
