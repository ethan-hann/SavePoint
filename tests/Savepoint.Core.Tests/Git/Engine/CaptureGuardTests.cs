using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

public sealed class CaptureGuardTests : IDisposable
{
    private readonly EngineWorkspace _ws = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    [Fact]
    public async Task A_capture_refuses_a_folder_without_a_marker_and_writes_nothing()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");
        File.Delete(EngineWorkspace.MarkerPath(repo));

        var error = await Assert.ThrowsAsync<ConsentMarkerException>(() => _ws.CaptureAsync(repo));

        Assert.Equal("error.consentMarker.missing", error.MessageId);
        Assert.Null(await _ws.ResolveAsync(repo, "HEAD"));
    }

    [Fact]
    public async Task A_capture_refuses_a_folder_another_game_holds()
    {
        var holder = await _ws.InitializedRepoAsync("other-game");
        var intruder = EngineWorkspace.Claim(holder, "game-1", RepoRole.Install);
        _ws.Write(holder, "a.txt", "a");

        var error = await Assert.ThrowsAsync<ConsentMarkerException>(() => _ws.CaptureAsync(intruder));

        Assert.Equal("error.consentMarker.conflict", error.MessageId);
        Assert.False(Directory.Exists(intruder.GitDir));
    }

    [Fact]
    public async Task A_capture_refuses_a_protected_work_tree()
    {
        var repo = await _ws.InitializedRepoAsync();
        var protectedRepo = repo with { WorkTree = _ws.Platform.Directories.HomeDir };
        Directory.CreateDirectory(protectedRepo.WorkTree);

        await Assert.ThrowsAsync<PathSafetyException>(() => _ws.CaptureAsync(protectedRepo));

        Assert.Null(await _ws.ResolveAsync(repo, "HEAD"));
    }

    [Fact]
    public async Task A_capture_refuses_when_the_storage_volume_is_low_before_writing_anything()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");
        var lockFile = Path.Combine(repo.GitDir, "index.lock");
        await File.WriteAllTextAsync(lockFile, string.Empty, Ct);
        var exclude = EngineWorkspace.GitDirText(repo, "info/exclude");
        _ws.Space.Free = 10;

        var error = await Assert.ThrowsAsync<DiskSpaceException>(() => _ws.CaptureAsync(repo, ignore: ["*.log"]));

        Assert.Equal("error.diskSpace.low", error.MessageId);
        Assert.Equal(100L * 1024 * 1024, error.Args["requiredBytes"]);
        Assert.Equal(10L, error.Args["freeBytes"]);
        Assert.True(File.Exists(lockFile), "nothing may be touched before the space check, not even a stale lock");
        Assert.Equal(exclude, EngineWorkspace.GitDirText(repo, "info/exclude"));
        Assert.Null(await _ws.ResolveAsync(repo, "HEAD"));
    }

    [Fact]
    public async Task A_capture_measures_the_storage_volume_not_the_game_folder_s()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");
        _ws.Space.PerPath = path => IsUnder(path, _ws.GamesRoot) ? 0 : null;

        var outcome = await _ws.CaptureAsync(repo);

        Assert.True(outcome.Changed);
        Assert.All(_ws.Space.Queries, query => Assert.True(IsUnder(query, _ws.StorageRoot), $"asked about {query}"));
        Assert.NotEmpty(_ws.Space.Queries);
    }

    [Fact]
    public async Task A_capture_with_plenty_of_space_on_the_storage_volume_ignores_a_full_game_volume()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");
        _ws.Space.PerPath = path => IsUnder(path, _ws.StorageRoot) ? long.MaxValue / 4 : 0;

        var outcome = await _ws.CaptureAsync(repo);

        Assert.True(outcome.Changed);
    }

    [Fact]
    public async Task A_cancelled_token_before_the_capture_starts_throws_without_touching_anything()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var error = await Assert.ThrowsAsync<OperationCancelledByUserException>(
            () => _ws.Engine.CaptureAsync(repo, new CaptureRequest("profile-1", "x", "id"), ct: cancelled.Token));

        Assert.Equal("error.capture.cancelled", error.MessageId);
        Assert.Equal("game-1", error.Args["gameId"]);
        Assert.Null(await _ws.ResolveAsync(repo, "HEAD"));
    }

    [Fact]
    public async Task A_capture_cancelled_while_git_is_adding_files_leaves_a_state_the_next_capture_completes()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.WriteMany(repo, "Data", 3000);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var progress = new ProgressLog { OnReport = _ => cancel.Cancel() };

        var error = await Assert.ThrowsAsync<OperationCancelledByUserException>(
            () => _ws.Engine.CaptureAsync(repo, new CaptureRequest("profile-1", "A point", "id-1"), progress, cancel.Token));

        Assert.Equal("error.capture.cancelled", error.MessageId);
        Assert.Null(await _ws.ResolveAsync(repo, "HEAD"));

        var outcome = await _ws.CaptureAsync(repo, "profile-1");

        Assert.True(outcome.Changed);
        Assert.Equal(3000, (await _ws.TreeAsync(repo, "profile-1")).Length);
        Assert.False(File.Exists(Path.Combine(repo.GitDir, "index.lock")));
    }

    private static bool IsUnder(string path, string root) =>
        Path.GetFullPath(path).StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase);
}
