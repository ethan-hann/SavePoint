using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Tests.Git.Engine;

[Collection(EngineCollections.A)]
public sealed class StatusTests : IDisposable
{
    private readonly EngineWorkspace _ws = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    [Fact]
    public async Task A_folder_is_clean_right_after_a_capture()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");
        var outcome = await _ws.CaptureAsync(repo, "profile-1");

        var status = await _ws.Engine.GetStatusAsync(repo, Ct);

        Assert.True(status.IsClean);
        Assert.Equal("profile-1", status.Branch);
        Assert.False(status.IsDetached);
        Assert.Equal(outcome.TipCommitId, status.HeadCommit);
        Assert.Empty(status.ChangedPaths);
        Assert.Empty(status.UntrackedPaths);
    }

    [Fact]
    public async Task Files_rewritten_with_the_same_bytes_are_not_changes()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "slot1.sav", "save one");
        _ws.Write(repo, "slot2.sav", "save two");
        await _ws.CaptureAsync(repo, "profile-1");

        // A new modified time with the same content, as a game rewriting its saves leaves behind.
        foreach (var name in new[] { "slot1.sav", "slot2.sav" })
        {
            var path = _ws.Write(repo, name, name == "slot1.sav" ? "save one" : "save two");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(30));
        }

        var status = await _ws.Engine.GetStatusAsync(repo, Ct);

        Assert.Empty(status.ChangedPaths);
        Assert.True(status.IsClean);
    }

    [Fact]
    public async Task Edits_deletions_and_new_files_make_it_dirty_and_are_listed()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "edited.txt", "one");
        _ws.Write(repo, "deleted.txt", "two");
        _ws.Write(repo, "kept.txt", "three");
        await _ws.CaptureAsync(repo, "profile-1");
        _ws.Write(repo, "edited.txt", "changed");
        _ws.Delete(repo, "deleted.txt");
        _ws.Write(repo, "Data/new file.txt", "new");

        var status = await _ws.Engine.GetStatusAsync(repo, Ct);

        Assert.False(status.IsClean);
        Assert.Equal(["deleted.txt", "edited.txt"], status.ChangedPaths.Order());
        Assert.Equal(["Data/new file.txt"], status.UntrackedPaths);
    }

    [Fact]
    public async Task Names_with_spaces_and_non_ascii_characters_come_back_exactly()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");
        await _ws.CaptureAsync(repo, "profile-1");
        _ws.Write(repo, "caf\u00e9/cr\u00e8me br\u00fbl\u00e9e.txt", "x");
        _ws.Write(repo, "it's [draft] (1).txt", "y");

        var status = await _ws.Engine.GetStatusAsync(repo, Ct);

        Assert.Equal(["caf\u00e9/cr\u00e8me br\u00fbl\u00e9e.txt", "it's [draft] (1).txt"], status.UntrackedPaths.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task The_marker_and_ignored_files_are_not_changes()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");
        _ws.Write(repo, "game.log", "log");
        await _ws.CaptureAsync(repo, "profile-1", ignore: ["*.log"]);
        _ws.Write(repo, "game.log", "more log");

        var status = await _ws.Engine.GetStatusAsync(repo, Ct);

        Assert.True(status.IsClean);
    }

    [Fact]
    public async Task A_folder_on_an_older_point_is_reported_as_detached_with_that_point()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "one");
        var first = await _ws.CaptureAsync(repo, "profile-1");
        _ws.Write(repo, "a.txt", "two");
        await _ws.CaptureAsync(repo, "profile-1");
        await _ws.GitWriteAsync(repo, "checkout", "--quiet", "--detach", first.TipCommitId!);

        var status = await _ws.Engine.GetStatusAsync(repo, Ct);

        Assert.True(status.IsDetached);
        Assert.Null(status.Branch);
        Assert.Equal(first.TipCommitId, status.HeadCommit);
        Assert.True(status.IsClean);
    }

    [Fact]
    public async Task A_repository_with_no_point_yet_has_no_head_commit_and_lists_the_files_as_new()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");

        var status = await _ws.Engine.GetStatusAsync(repo, Ct);

        Assert.Null(status.HeadCommit);
        Assert.False(status.IsDetached);
        Assert.False(status.IsClean);
        Assert.Equal(["a.txt"], status.UntrackedPaths);
    }

    [Fact]
    public async Task Status_takes_no_lock_while_a_capture_holds_the_index()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");
        await _ws.CaptureAsync(repo, "profile-1");
        var lockFile = Path.Combine(repo.GitDir, "index.lock");
        await File.WriteAllTextAsync(lockFile, string.Empty, Ct);

        var status = await _ws.Engine.GetStatusAsync(repo, Ct);

        Assert.True(status.IsClean);
        Assert.True(File.Exists(lockFile), "a read must neither wait for nor remove another operation's lock");
        Assert.All(_ws.Invocations.Where(invocation => invocation.Arguments[0] == "status"), invocation => Assert.True(invocation.ReadOnly));
    }

    [Fact]
    public async Task Status_refuses_a_folder_without_this_games_marker()
    {
        var repo = await _ws.InitializedRepoAsync();
        File.Delete(EngineWorkspace.MarkerPath(repo));

        await Assert.ThrowsAsync<ConsentMarkerException>(() => _ws.Engine.GetStatusAsync(repo, Ct));
        await Assert.ThrowsAsync<ConsentMarkerException>(() => _ws.Engine.GetStatusAsync(EngineWorkspace.Claim(repo, "other", RepoRole.External), Ct));
    }

    [Fact]
    public async Task Status_refuses_a_protected_or_missing_work_tree()
    {
        var repo = await _ws.InitializedRepoAsync();
        var protectedRepo = repo with { WorkTree = _ws.Platform.Directories.HomeDir };
        Directory.CreateDirectory(protectedRepo.WorkTree);

        await Assert.ThrowsAsync<PathSafetyException>(() => _ws.Engine.GetStatusAsync(protectedRepo, Ct));
        await Assert.ThrowsAsync<PathMissingException>(() => _ws.Engine.GetStatusAsync(repo with { WorkTree = Path.Combine(_ws.GamesRoot, "gone") }, Ct));
    }
}
