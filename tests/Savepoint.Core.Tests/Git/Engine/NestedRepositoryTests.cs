using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// A folder in the game that keeps its own history (a mod cloned from a code host) cannot be stored inside another one:
// git would record an empty pointer, or fail. A capture or preview refuses before it stages anything.
[Collection(EngineCollections.F)]
public sealed class NestedRepositoryTests : IDisposable
{
    private readonly EngineWorkspace _ws = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    // The least git accepts as a repository: HEAD, objects and refs.
    private static void MakeNestedRepository(EngineWorkspace ws, RepoRef repo, string folder)
    {
        var dot = ws.FileOf(repo, folder + "/.git");
        Directory.CreateDirectory(Path.Combine(dot, "objects"));
        Directory.CreateDirectory(Path.Combine(dot, "refs"));
        File.WriteAllText(Path.Combine(dot, "HEAD"), "ref: refs/heads/main\n");
    }

    private bool Staged() => _ws.Invocations.Any(invocation => invocation.Arguments.Contains("add") || invocation.Arguments.Contains("commit"));

    [Fact]
    public async Task A_capture_refuses_a_folder_that_is_a_repository_and_names_its_hidden_folder()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");
        _ws.Write(repo, "mod/file.txt", "mod");
        MakeNestedRepository(_ws, repo, "mod");

        var error = await Assert.ThrowsAsync<NestedRepositoryException>(() => _ws.CaptureAsync(repo, "profile-1"));

        Assert.Equal("error.capture.nestedRepository", error.MessageId);
        Assert.Equal(_ws.FileOf(repo, "mod/.git"), error.Args["path"]);
        Assert.Equal(1, error.Args["count"]);
    }

    [Fact]
    public async Task A_refused_capture_stages_and_records_nothing()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");
        MakeNestedRepository(_ws, repo, "mod");

        await Assert.ThrowsAsync<NestedRepositoryException>(() => _ws.CaptureAsync(repo, "profile-1"));

        Assert.False(Staged(), "nothing may be staged or committed before the refusal");
        Assert.Null(await _ws.TipAsync(repo, "profile-1"));
    }

    [Fact]
    public async Task A_hidden_file_that_points_elsewhere_is_refused_too()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");
        _ws.Write(repo, "mod/inner/file.txt", "mod");
        _ws.Write(repo, "mod/.git", "gitdir: ../../elsewhere\n");

        var error = await Assert.ThrowsAsync<NestedRepositoryException>(() => _ws.CaptureAsync(repo, "profile-1"));

        Assert.Equal(_ws.FileOf(repo, "mod/.git"), error.Args["path"]);
        Assert.False(Staged());
    }

    [Fact]
    public async Task A_repository_deep_in_the_tree_beside_ordinary_files_is_found()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");
        _ws.Write(repo, "Data/Mods/pack/readme.txt", "pack");
        _ws.Write(repo, "Data/Mods/pack/sub/deeper.txt", "deeper");
        MakeNestedRepository(_ws, repo, "Data/Mods/pack");

        var error = await Assert.ThrowsAsync<NestedRepositoryException>(() => _ws.CaptureAsync(repo, "profile-1"));

        Assert.Equal(_ws.FileOf(repo, "Data/Mods/pack/.git"), error.Args["path"]);
    }

    [Fact]
    public async Task The_game_folder_being_a_repository_itself_is_refused()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");
        MakeNestedRepository(_ws, repo, ".");

        var error = await Assert.ThrowsAsync<NestedRepositoryException>(() => _ws.CaptureAsync(repo, "profile-1"));

        Assert.Equal(_ws.FileOf(repo, ".git"), error.Args["path"]);
    }

    [Fact]
    public async Task Every_one_found_is_counted_and_the_first_in_path_order_is_named()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");
        _ws.Write(repo, "zeta/file.txt", "z");
        _ws.Write(repo, "alpha/file.txt", "a");
        _ws.Write(repo, "mid/inner/file.txt", "m");
        MakeNestedRepository(_ws, repo, "zeta");
        MakeNestedRepository(_ws, repo, "alpha");
        _ws.Write(repo, "mid/.git", "gitdir: nowhere\n");

        var error = await Assert.ThrowsAsync<NestedRepositoryException>(() => _ws.CaptureAsync(repo, "profile-1"));

        Assert.Equal(3, error.Args["count"]);
        Assert.Equal(_ws.FileOf(repo, "alpha/.git"), error.Args["path"]);
    }

    [Fact]
    public async Task A_folder_the_exclude_list_names_may_be_a_repository()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");
        _ws.Write(repo, "mod/file.txt", "mod");
        MakeNestedRepository(_ws, repo, "mod");

        await _ws.CaptureAsync(repo, "profile-1", ignore: ["/mod/"]);

        Assert.Equal(["a.txt"], await _ws.TreeAsync(repo, "profile-1"));
    }

    [Fact]
    public async Task Once_the_hidden_folder_is_moved_away_the_same_capture_succeeds()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");
        _ws.Write(repo, "mod/file.txt", "mod");
        MakeNestedRepository(_ws, repo, "mod");
        await Assert.ThrowsAsync<NestedRepositoryException>(() => _ws.CaptureAsync(repo, "profile-1"));

        Directory.Delete(_ws.FileOf(repo, "mod/.git"), recursive: true);
        var outcome = await _ws.CaptureAsync(repo, "profile-1");

        Assert.True(outcome.Changed);
        Assert.Equal(["a.txt", "mod/file.txt"], await _ws.TreeAsync(repo, "profile-1"));
    }

    [Theory]
    [InlineData(CaptureMode.Amend)]
    [InlineData(CaptureMode.Fork)]
    [InlineData(CaptureMode.MoveForward)]
    public async Task Every_capture_mode_refuses_before_it_changes_anything(CaptureMode mode)
    {
        var (repo, points) = await _ws.TwoPointsRepoAsync();
        MakeNestedRepository(_ws, repo, "mod");
        _ws.Write(repo, "mod/file.txt", "mod");
        var before = await _ws.TipAsync(repo, "profile-1");
        var request = new CaptureRequest(mode == CaptureMode.Fork ? "profile-2" : "profile-1", "Next", Guid.NewGuid().ToString())
        {
            Mode = mode,
            ExpectedStartCommit = points[^1],
        };

        await Assert.ThrowsAsync<NestedRepositoryException>(() => _ws.Engine.CaptureAsync(repo, request, null, Ct));

        Assert.Equal(before, await _ws.TipAsync(repo, "profile-1"));
        Assert.Null(await _ws.TipAsync(repo, "profile-2"));
    }

    [Fact]
    public async Task The_capture_candidates_refuse_it_too()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");
        _ws.Write(repo, "mod/file.txt", "mod");
        MakeNestedRepository(_ws, repo, "mod");

        var error = await Assert.ThrowsAsync<NestedRepositoryException>(() => _ws.Engine.ListCaptureCandidatesAsync(repo, [], Ct));

        Assert.Equal(_ws.FileOf(repo, "mod/.git"), error.Args["path"]);
    }

    [Fact]
    public async Task Status_and_discard_leave_the_repository_alone_and_do_not_fail()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");
        await _ws.CaptureAsync(repo, "profile-1");
        _ws.Write(repo, "mod/file.txt", "mod");
        MakeNestedRepository(_ws, repo, "mod");

        var status = await _ws.Engine.GetStatusAsync(repo, Ct);
        await _ws.Engine.DiscardChangesAsync(repo, Ct);

        Assert.Empty(status.UntrackedPaths);
        Assert.True(File.Exists(_ws.FileOf(repo, "mod/file.txt")));
        Assert.True(Directory.Exists(_ws.FileOf(repo, "mod/.git")));
    }
}
