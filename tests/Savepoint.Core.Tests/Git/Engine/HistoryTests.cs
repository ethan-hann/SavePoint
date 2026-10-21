using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// One repository built once and only read: profile-1 has three points, profile-2 branched from its second and added a fourth.
public sealed class HistoryFixture : IAsyncLifetime
{
    internal EngineWorkspace Workspace { get; } = new();

    public RepoRef Repo { get; private set; } = null!;

    public RepoRef Empty { get; private set; } = null!;

    public string[] Profile1 { get; private set; } = [];

    public string Fourth { get; private set; } = string.Empty;

    public DateTimeOffset Started { get; private set; }

    public async ValueTask InitializeAsync()
    {
        Started = DateTimeOffset.UtcNow.AddSeconds(-5);
        Repo = await Workspace.InitializedRepoAsync();
        Workspace.Write(Repo, "a.txt", "one");
        var first = await Capture("profile-1", "First point", "cap-1");
        Workspace.Write(Repo, "b.txt", "two");
        var second = await Capture("profile-1", "Second point", "cap-2");
        Workspace.Write(Repo, "a.txt", "three");
        var third = await Capture("profile-1", "Third point", "cap-3");
        Profile1 = [first, second, third];

        await Workspace.GitWriteAsync(Repo, "branch", "profile-2", second);
        await Workspace.GitWriteAsync(Repo, "checkout", "--quiet", "profile-2");
        Workspace.Write(Repo, "c.txt", "four");
        Fourth = await Capture("profile-2", "Fourth point", "cap-4");
        await Workspace.GitWriteAsync(Repo, "checkout", "--quiet", "profile-1");

        Empty = await Workspace.InitializedRepoAsync("game-2");
    }

    public ValueTask DisposeAsync()
    {
        Workspace.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<string> Capture(string branch, string message, string captureId)
    {
        var outcome = await Workspace.Engine.CaptureAsync(Repo, new CaptureRequest(branch, message, captureId), ct: TestContext.Current.CancellationToken);
        return outcome.TipCommitId!;
    }
}

public sealed class HistoryTests : IClassFixture<HistoryFixture>
{
    private readonly HistoryFixture _fx;

    public HistoryTests(HistoryFixture fixture)
    {
        _fx = fixture;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private GitEngine Engine => _fx.Workspace.Engine;

    [Fact]
    public async Task Commits_are_listed_newest_first_with_id_subject_date_and_capture_id()
    {
        var commits = await Engine.ListCommitsAsync(_fx.Repo, "profile-1", ct: Ct);

        Assert.Equal(_fx.Profile1.Reverse(), commits.Select(commit => commit.Id));
        Assert.Equal(["Third point", "Second point", "First point"], commits.Select(commit => commit.Subject));
        Assert.Equal(["cap-3", "cap-2", "cap-1"], commits.Select(commit => commit.CaptureId));
        Assert.All(commits, commit => Assert.InRange(commit.Date, _fx.Started, DateTimeOffset.UtcNow.AddMinutes(1)));
        Assert.Equal(commits.OrderByDescending(commit => commit.Date), commits);
    }

    [Fact]
    public async Task A_branch_lists_only_the_points_reachable_from_it()
    {
        var commits = await Engine.ListCommitsAsync(_fx.Repo, "profile-2", ct: Ct);

        Assert.Equal([_fx.Fourth, _fx.Profile1[1], _fx.Profile1[0]], commits.Select(commit => commit.Id));
    }

    [Fact]
    public async Task The_list_can_be_limited()
    {
        var commits = await Engine.ListCommitsAsync(_fx.Repo, "profile-1", maxCount: 2, ct: Ct);

        Assert.Equal(["Third point", "Second point"], commits.Select(commit => commit.Subject));
    }

    [Fact]
    public async Task A_non_positive_limit_is_rejected()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Engine.ListCommitsAsync(_fx.Repo, "profile-1", maxCount: 0, ct: Ct));
    }

    [Fact]
    public async Task A_branch_that_does_not_exist_has_no_points()
    {
        Assert.Empty(await Engine.ListCommitsAsync(_fx.Repo, "no-such-branch", ct: Ct));
        Assert.Equal(0, await Engine.CountCommitsAsync(_fx.Repo, "no-such-branch", Ct));
        Assert.Null(await Engine.GetBranchTipAsync(_fx.Repo, "no-such-branch", Ct));
        Assert.False(await Engine.BranchExistsAsync(_fx.Repo, "no-such-branch", Ct));
    }

    [Fact]
    public async Task A_repository_that_does_not_exist_has_no_points_or_branches()
    {
        var repo = _fx.Workspace.NewRepo();

        Assert.Empty(await Engine.ListCommitsAsync(repo, "profile-1", ct: Ct));
        Assert.Equal(0, await Engine.CountCommitsAsync(repo, "profile-1", Ct));
        Assert.Null(await Engine.GetBranchTipAsync(repo, "profile-1", Ct));
        Assert.False(await Engine.BranchExistsAsync(repo, "profile-1", Ct));
        Assert.Empty(await Engine.ListBranchesAsync(repo, Ct));
    }

    [Fact]
    public async Task Points_are_counted()
    {
        Assert.Equal(3, await Engine.CountCommitsAsync(_fx.Repo, "profile-1", Ct));
        Assert.Equal(3, await Engine.CountCommitsAsync(_fx.Repo, "profile-2", Ct));
    }

    [Fact]
    public async Task The_tip_is_the_newest_full_commit_id()
    {
        Assert.Equal(_fx.Profile1[2], await Engine.GetBranchTipAsync(_fx.Repo, "profile-1", Ct));
        Assert.Equal(_fx.Fourth, await Engine.GetBranchTipAsync(_fx.Repo, "profile-2", Ct));
    }

    [Fact]
    public async Task A_branch_with_points_exists()
    {
        Assert.True(await Engine.BranchExistsAsync(_fx.Repo, "profile-1", Ct));
    }

    [Fact]
    public async Task Branches_are_listed_sorted()
    {
        Assert.Equal(["profile-1", "profile-2"], await Engine.ListBranchesAsync(_fx.Repo, Ct));
    }

    [Fact]
    public async Task A_repository_with_no_point_yet_lists_no_branches_and_the_branch_it_would_start_does_not_exist()
    {
        Assert.Empty(await Engine.ListBranchesAsync(_fx.Empty, Ct));
        Assert.False(await Engine.BranchExistsAsync(_fx.Empty, "profile-1", Ct));
        Assert.Empty(await Engine.ListCommitsAsync(_fx.Empty, "profile-1", ct: Ct));
    }

    [Fact]
    public async Task Points_held_by_no_other_branch_are_the_ones_deleting_would_lose()
    {
        var onlyProfile1 = await Engine.ListCommitsOnlyOnAsync(_fx.Repo, "profile-1", ["profile-2"], Ct);
        var onlyProfile2 = await Engine.ListCommitsOnlyOnAsync(_fx.Repo, "profile-2", ["profile-1"], Ct);

        Assert.Equal([_fx.Profile1[2]], onlyProfile1.Select(commit => commit.Id));
        Assert.Equal(["cap-3"], onlyProfile1.Select(commit => commit.CaptureId));
        Assert.Equal([_fx.Fourth], onlyProfile2.Select(commit => commit.Id));
    }

    [Fact]
    public async Task With_no_other_branch_every_point_is_unique()
    {
        var all = await Engine.ListCommitsOnlyOnAsync(_fx.Repo, "profile-1", [], Ct);

        Assert.Equal(_fx.Profile1.Reverse(), all.Select(commit => commit.Id));
    }

    [Fact]
    public async Task Other_branches_that_do_not_exist_and_the_branch_itself_are_ignored()
    {
        var unique = await Engine.ListCommitsOnlyOnAsync(_fx.Repo, "profile-1", ["profile-1", "ghost", "profile-2"], Ct);

        Assert.Equal([_fx.Profile1[2]], unique.Select(commit => commit.Id));
    }

    [Fact]
    public async Task Unique_points_of_a_branch_that_does_not_exist_are_empty()
    {
        Assert.Empty(await Engine.ListCommitsOnlyOnAsync(_fx.Repo, "ghost", ["profile-1"], Ct));
    }

    [Fact]
    public async Task Reads_run_with_optional_locks_off_and_never_start_a_write()
    {
        _ = await Engine.ListCommitsAsync(_fx.Repo, "profile-1", ct: Ct);
        _ = await Engine.CountCommitsAsync(_fx.Repo, "profile-2", Ct);
        _ = await Engine.ListBranchesAsync(_fx.Repo, Ct);

        var reads = _fx.Workspace.Invocations.Where(invocation => invocation.Arguments[0] is "log" or "rev-list" or "for-each-ref").ToArray();
        Assert.NotEmpty(reads);
        Assert.All(reads, invocation => Assert.True(invocation.ReadOnly, string.Join(' ', invocation.Arguments)));
    }
}
