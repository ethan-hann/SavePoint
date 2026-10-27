using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// A branch can start at a copy of a point under a new capture id, so History lists the copy as a point of its own.
[Collection(EngineCollections.E)]
public sealed class BranchCopyTests : IDisposable
{
    private const string _emptyFolder = "archive/pc/mod";

    private readonly EngineWorkspace _ws = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    private Task<string> TrailerAsync(RepoRef repo, string revision, string key) =>
        _ws.GitOutAsync(repo, "log", "-1", $"--format=%(trailers:key={key},valueonly)", revision, "--");

    private Task<string> FieldAsync(RepoRef repo, string revision, string format) =>
        _ws.GitOutAsync(repo, "log", "-1", "--format=" + format, revision, "--");

    // Amends the newest point of profile-1 so the commit it replaces is kept as a record.
    private async Task<(RepoRef Repo, string Old, RemovedCommit Record)> ReplacedTipAsync()
    {
        var (repo, points) = await _ws.ThreePointsRepoAsync();
        _ws.Write(repo, "a.txt", "played on");
        await _ws.Engine.CaptureAsync(repo, new CaptureRequest("profile-1", "Third point", Guid.NewGuid().ToString()) { Mode = CaptureMode.Amend }, ct: Ct);
        var record = Assert.Single(await _ws.Engine.ListRemovedAsync(repo, Ct));
        return (repo, points[2], record);
    }

    [Fact]
    public async Task The_copy_holds_the_same_files_parents_name_and_capture_time_under_a_new_capture_id()
    {
        var (repo, points) = await _ws.ThreePointsRepoAsync();

        var copy = await _ws.Engine.CreateBranchAsCopyAsync(repo, "copied", points[2], "new-id", ct: Ct);

        Assert.Equal(copy, await _ws.TipAsync(repo, "copied"));
        Assert.NotEqual(points[2], copy);
        Assert.Equal(await FieldAsync(repo, points[2], "%T"), await FieldAsync(repo, copy, "%T"));
        Assert.Equal(await FieldAsync(repo, points[2], "%P"), await FieldAsync(repo, copy, "%P"));
        Assert.Equal("Third point", await FieldAsync(repo, copy, "%s"));
        Assert.Equal("new-id", await TrailerAsync(repo, copy, "Savepoint-Capture"));
        Assert.Equal(await TrailerAsync(repo, points[2], "Savepoint-Captured-At"), await TrailerAsync(repo, copy, "Savepoint-Captured-At"));
        var info = (await _ws.Engine.ListCommitsAsync(repo, "copied", ct: Ct)).First();
        Assert.Equal("new-id", info.CaptureId);
    }

    [Fact]
    public async Task The_copy_keeps_the_empty_folders_the_point_was_taken_with()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "game.exe", "exe");
        Directory.CreateDirectory(_ws.FileOf(repo, _emptyFolder));
        var point = (await _ws.CaptureAsync(repo, "profile-1")).TipCommitId!;

        var copy = await _ws.Engine.CreateBranchAsCopyAsync(repo, "copied", point, "new-id", ct: Ct);

        Assert.Equal("[\"archive/pc/mod\"]", await TrailerAsync(repo, copy, "Savepoint-Empty-Folders"));
    }

    [Fact]
    public async Task The_copy_keeps_the_author_and_committer_dates_of_the_point()
    {
        var (repo, points) = await _ws.ThreePointsRepoAsync();

        // Dates are whole seconds, so a pause is what makes a copy that took the clock show.
        await GitClock.NextSecondAsync(Ct);
        var copy = await _ws.Engine.CreateBranchAsCopyAsync(repo, "copied", points[2], "new-id", ct: Ct);

        Assert.Equal(await FieldAsync(repo, points[2], "%aI"), await FieldAsync(repo, copy, "%aI"));
        Assert.Equal(await FieldAsync(repo, points[2], "%cI"), await FieldAsync(repo, copy, "%cI"));
    }

    [Fact]
    public async Task The_copy_keeps_the_utc_offsets_the_point_was_made_with()
    {
        var (repo, points) = await _ws.ThreePointsRepoAsync();
        var tree = await _ws.GitOutAsync(repo, "rev-parse", points[2] + "^{tree}");
        var made = await _ws.Runner.RunAsync(
            new GitInvocation(["commit-tree", tree, "-F", "-"])
            {
                GitDir = repo.GitDir,
                WorkTree = repo.WorkTree,
                Stdin = "Offset point\n\nSavepoint-Capture: cap-offset\nSavepoint-Captured-At: 1\n",
                ExtraEnvironment = new Dictionary<string, string?>
                {
                    ["GIT_AUTHOR_NAME"] = "Author",
                    ["GIT_AUTHOR_EMAIL"] = "author@example.invalid",
                    ["GIT_COMMITTER_NAME"] = "Committer",
                    ["GIT_COMMITTER_EMAIL"] = "committer@example.invalid",
                    ["GIT_AUTHOR_DATE"] = "2026-01-02T03:04:05+09:00",
                    ["GIT_COMMITTER_DATE"] = "2026-01-02T03:04:05-03:30",
                },
            },
            Ct);
        var offsetPoint = made.StandardOutput.Trim();

        var copy = await _ws.Engine.CreateBranchAsCopyAsync(repo, "copied", offsetPoint, "new-id", ct: Ct);

        Assert.Equal("2026-01-02T03:04:05+09:00", await FieldAsync(repo, copy, "%aI"));
        Assert.Equal("2026-01-02T03:04:05-03:30", await FieldAsync(repo, copy, "%cI"));
    }

    [Fact]
    public async Task A_record_deleted_after_it_was_read_refuses_the_copy_and_nothing_moves()
    {
        var (repo, old, record) = await ReplacedTipAsync();
        await _ws.Engine.ClearRemovedAsync(repo, [record.RefName], Ct);

        await Assert.ThrowsAsync<GitOperationException>(() => _ws.Engine.CreateBranchAsCopyAsync(repo, "copied", old, "new-id", record, Ct));

        Assert.Null(await _ws.TipAsync(repo, "copied"));
        Assert.Empty(await _ws.Engine.ListRemovedAsync(repo, Ct));
    }

    [Fact]
    public async Task A_branch_that_exists_is_refused_and_no_commit_is_written()
    {
        var (repo, points) = await _ws.ThreePointsRepoAsync();
        var tip = await _ws.TipAsync(repo, "profile-1");
        var before = await _ws.GitOutAsync(repo, "count-objects", "-v");

        await Assert.ThrowsAsync<ProfileAlreadyExistsException>(() => _ws.Engine.CreateBranchAsCopyAsync(repo, "profile-1", points[1], "new-id", ct: Ct));

        Assert.Equal(tip, await _ws.TipAsync(repo, "profile-1"));
        Assert.Equal(before, await _ws.GitOutAsync(repo, "count-objects", "-v"));
    }

    [Fact]
    public async Task A_commit_with_no_capture_id_is_refused_and_no_branch_is_made()
    {
        var repo = await _ws.InitializedRepoAsync();
        await _ws.Engine.CreateEmptyBranchAsync(repo, "plain", ct: Ct);
        var plain = (await _ws.TipAsync(repo, "plain"))!;

        await Assert.ThrowsAsync<GitOperationException>(() => _ws.Engine.CreateBranchAsCopyAsync(repo, "copied", plain, "new-id", ct: Ct));

        Assert.Null(await _ws.TipAsync(repo, "copied"));
    }

    [Fact]
    public async Task A_folder_without_this_games_marker_is_refused()
    {
        var (repo, points) = await _ws.ThreePointsRepoAsync();
        File.Delete(EngineWorkspace.MarkerPath(repo));

        await Assert.ThrowsAsync<ConsentMarkerException>(() => _ws.Engine.CreateBranchAsCopyAsync(repo, "copied", points[2], "new-id", ct: Ct));
    }

    [Fact]
    public async Task The_record_of_the_copied_commit_moves_to_the_copy_in_the_same_step_as_the_branch()
    {
        var (repo, old, record) = await ReplacedTipAsync();

        var copy = await _ws.Engine.CreateBranchAsCopyAsync(repo, "copied", old, "new-id", record, Ct);

        var records = await _ws.Engine.ListRemovedAsync(repo, Ct);
        var moved = Assert.Single(records);
        Assert.Equal(copy, moved.Commit.Id);
        Assert.Equal(record.Branch, moved.Branch);
        Assert.Equal(record.Reason, moved.Reason);
        Assert.Equal(record.RemovedAt, moved.RemovedAt);
        Assert.Equal("new-id", moved.Commit.CaptureId);
    }

    [Fact]
    public async Task A_refused_branch_leaves_the_record_where_it_was()
    {
        var (repo, old, record) = await ReplacedTipAsync();

        await Assert.ThrowsAsync<ProfileAlreadyExistsException>(() => _ws.Engine.CreateBranchAsCopyAsync(repo, "profile-1", old, "new-id", record, Ct));

        Assert.Equal(record.RefName, Assert.Single(await _ws.Engine.ListRemovedAsync(repo, Ct)).RefName);
    }
}
