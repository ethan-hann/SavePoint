using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

[Collection(EngineCollections.E)]
public sealed class DiscardTests : IDisposable
{
    private readonly EngineWorkspace _ws = new(options => options with { ResyncDelays = [TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(20)] });

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    [Fact]
    public async Task Discard_brings_back_edits_and_deletions_and_removes_new_files_in_one_call()
    {
        var (repo, committed) = await ThreePointsAndMessAsync();

        await _ws.Engine.DiscardChangesAsync(repo, Ct);

        Assert.Equal(committed, TreeSnapshot.Take(repo.WorkTree));
        Assert.True((await _ws.Engine.GetStatusAsync(repo, Ct)).IsClean);
    }

    [Fact]
    public async Task Discard_keeps_the_marker_and_ignored_files()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");
        await _ws.CaptureAsync(repo, "profile-1", ignore: ["*.log"]);
        _ws.Write(repo, "game.log", "important log");
        _ws.Write(repo, "new.txt", "new");

        await _ws.Engine.DiscardChangesAsync(repo, Ct);

        Assert.True(File.Exists(_ws.FileOf(repo, "game.log")));
        Assert.False(File.Exists(_ws.FileOf(repo, "new.txt")));
        Assert.Equal("game-1", EngineWorkspace.RequireMarker(repo).GameId);
    }

    [Fact]
    public async Task Discard_works_from_an_older_point_where_there_is_no_branch_to_name()
    {
        var (repo, points) = await _ws.OlderPointRepoAsync();
        var older = TreeSnapshot.Take(repo.WorkTree);
        _ws.Write(repo, "a.txt", "played");
        _ws.Write(repo, "played.txt", "played");

        await _ws.Engine.DiscardChangesAsync(repo, Ct);

        Assert.Equal(older, TreeSnapshot.Take(repo.WorkTree));
        Assert.True((await _ws.Engine.GetStatusAsync(repo, Ct)).IsDetached);
    }

    [Fact]
    public async Task Discard_clears_a_stale_index_lock()
    {
        var (repo, committed) = await ThreePointsAndMessAsync();
        var lockFile = Path.Combine(repo.GitDir, "index.lock");
        await File.WriteAllTextAsync(lockFile, string.Empty, Ct);

        await _ws.Engine.DiscardChangesAsync(repo, Ct);

        Assert.Equal(committed, TreeSnapshot.Take(repo.WorkTree));
        Assert.False(File.Exists(lockFile));
    }

    [Fact]
    public async Task Discard_has_nothing_to_return_to_in_a_repository_with_no_point_and_removes_nothing()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "precious.txt", "my own files");

        var error = await Assert.ThrowsAsync<ProfileHasNoPointsException>(() => _ws.Engine.DiscardChangesAsync(repo, Ct));

        Assert.Equal("error.profile.noPoints", error.MessageId);
        Assert.Equal("my own files", await File.ReadAllTextAsync(_ws.FileOf(repo, "precious.txt"), Ct));
        Assert.True(File.Exists(EngineWorkspace.MarkerPath(repo)));
    }

    [Fact]
    public async Task Clean_has_nothing_to_keep_files_against_in_a_repository_with_no_point_and_removes_nothing()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "Saves/slot1.sav", "my own saves");
        _ws.Write(repo, "settings.ini", "my own settings");
        var before = TreeSnapshot.Take(repo.WorkTree);

        var error = await Assert.ThrowsAsync<ProfileHasNoPointsException>(() => _ws.Engine.CleanAsync(repo, Ct));

        Assert.Equal("error.profile.noPoints", error.MessageId);
        Assert.Equal(before, TreeSnapshot.Take(repo.WorkTree));
    }

    [Fact]
    public async Task Discard_refuses_a_folder_without_this_games_marker_and_a_protected_work_tree()
    {
        var (repo, _) = await ThreePointsAndMessAsync();
        var mess = TreeSnapshot.Take(repo.WorkTree);
        var protectedRepo = repo with { WorkTree = _ws.Platform.Directories.HomeDir };
        Directory.CreateDirectory(protectedRepo.WorkTree);

        await Assert.ThrowsAsync<ConsentMarkerException>(() => _ws.Engine.DiscardChangesAsync(EngineWorkspace.Claim(repo, "other", RepoRole.External), Ct));
        await Assert.ThrowsAsync<PathSafetyException>(() => _ws.Engine.DiscardChangesAsync(protectedRepo, Ct));
        File.Delete(EngineWorkspace.MarkerPath(repo));
        await Assert.ThrowsAsync<ConsentMarkerException>(() => _ws.Engine.DiscardChangesAsync(repo, Ct));

        Assert.Equal(mess, TreeSnapshot.Take(repo.WorkTree));
    }

    [Fact]
    public async Task Discard_names_a_file_that_stays_in_use_and_a_repeat_after_release_finishes()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows refuses to replace a file another program holds open");
        var (repo, committed) = await ThreePointsAndMessAsync();
        var locked = _ws.FileOf(repo, "a.txt");

        var held = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        try
        {
            var error = await Assert.ThrowsAsync<GitOperationException>(() => _ws.Engine.DiscardChangesAsync(repo, Ct));

            Assert.Equal(locked, error.Args["path"]);
        }
        finally
        {
            await held.DisposeAsync();
        }

        await _ws.Engine.DiscardChangesAsync(repo, Ct);
        Assert.Equal(committed, TreeSnapshot.Take(repo.WorkTree));
    }

    [Fact]
    public async Task Clean_removes_new_files_and_folders_and_leaves_edits_ignored_files_and_the_marker()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");
        await _ws.CaptureAsync(repo, "profile-1", ignore: ["*.log"]);
        _ws.Write(repo, "a.txt", "edited");
        _ws.Write(repo, "game.log", "log");
        _ws.Write(repo, "NewFolder/new.txt", "new");
        _ws.Write(repo, "new.txt", "new");

        await _ws.Engine.CleanAsync(repo, Ct);

        Assert.False(File.Exists(_ws.FileOf(repo, "new.txt")));
        Assert.False(Directory.Exists(_ws.FileOf(repo, "NewFolder")));
        Assert.Equal("edited", await File.ReadAllTextAsync(_ws.FileOf(repo, "a.txt"), Ct));
        Assert.True(File.Exists(_ws.FileOf(repo, "game.log")));
        Assert.True(File.Exists(EngineWorkspace.MarkerPath(repo)));
    }

    [Fact]
    public async Task Clean_refuses_a_folder_without_this_games_marker_and_a_protected_work_tree()
    {
        var (repo, _) = await ThreePointsAndMessAsync();
        var protectedRepo = repo with { WorkTree = _ws.Platform.Directories.HomeDir };
        Directory.CreateDirectory(protectedRepo.WorkTree);

        await Assert.ThrowsAsync<ConsentMarkerException>(() => _ws.Engine.CleanAsync(EngineWorkspace.Claim(repo, "other", RepoRole.External), Ct));
        await Assert.ThrowsAsync<PathSafetyException>(() => _ws.Engine.CleanAsync(protectedRepo, Ct));

        Assert.True(File.Exists(_ws.FileOf(repo, "messy.txt")));
    }

    [Fact]
    public async Task Force_checkout_replaces_local_changes_that_a_plain_one_refuses()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "profile a");
        _ws.Write(repo, "only-a.txt", "A");
        await _ws.CaptureAsync(repo, "profile-a");
        var a = TreeSnapshot.Take(repo.WorkTree);
        await _ws.GitWriteAsync(repo, "checkout", "--quiet", "-b", "profile-b");
        _ws.Write(repo, "a.txt", "profile b");
        _ws.Delete(repo, "only-a.txt");
        await _ws.CaptureAsync(repo, "profile-b");
        _ws.Write(repo, "a.txt", "my unsaved edit");
        _ws.Write(repo, "only-a.txt", "my own file in the way");
        await Assert.ThrowsAsync<DirtyWorkingTreeException>(() => _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct));

        await _ws.Engine.ForceCheckoutBranchAsync(repo, "profile-a", Ct);

        Assert.Equal(a, TreeSnapshot.Take(repo.WorkTree));
        Assert.Equal("refs/heads/profile-a", await _ws.HeadRefAsync(repo));
        Assert.Null((await _ws.Engine.GetStatusAsync(repo, Ct)).InterruptedSwitch);
    }

    [Fact]
    public async Task Force_checkout_of_a_point_detaches_and_matches_it()
    {
        var (repo, points) = await _ws.ThreePointsRepoAsync();
        _ws.Write(repo, "a.txt", "unsaved");

        await _ws.Engine.ForceCheckoutCommitAsync(repo, points[0], Ct);

        Assert.Equal("one", await File.ReadAllTextAsync(_ws.FileOf(repo, "a.txt"), Ct));
        Assert.False(File.Exists(_ws.FileOf(repo, "b.txt")));
        Assert.True((await _ws.Engine.GetStatusAsync(repo, Ct)).IsDetached);
    }

    [Fact]
    public async Task Force_checkout_refuses_a_missing_marker_a_protected_work_tree_and_a_bad_target()
    {
        var (repo, _) = await ThreePointsAndMessAsync();
        var protectedRepo = repo with { WorkTree = _ws.Platform.Directories.HomeDir };
        Directory.CreateDirectory(protectedRepo.WorkTree);

        await Assert.ThrowsAsync<ConsentMarkerException>(() => _ws.Engine.ForceCheckoutBranchAsync(EngineWorkspace.Claim(repo, "other", RepoRole.External), "profile-1", Ct));
        await Assert.ThrowsAsync<PathSafetyException>(() => _ws.Engine.ForceCheckoutBranchAsync(protectedRepo, "profile-1", Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => _ws.Engine.ForceCheckoutCommitAsync(repo, "short", Ct));
        await Assert.ThrowsAsync<ProfileHasNoPointsException>(() => _ws.Engine.ForceCheckoutBranchAsync(repo, "ghost", Ct));

        Assert.True(File.Exists(_ws.FileOf(repo, "messy.txt")));
    }

    // Three points on profile-1, then an edit, a deletion and a new file on top; returns the folder as the last point had it.
    private async Task<(RepoRef Repo, SortedDictionary<string, string> Committed)> ThreePointsAndMessAsync()
    {
        var (repo, _) = await _ws.ThreePointsRepoAsync();
        var committed = TreeSnapshot.Take(repo.WorkTree);
        _ws.Write(repo, "a.txt", "edited by the player");
        _ws.Delete(repo, "b.txt");
        _ws.Write(repo, "messy.txt", "new file");
        _ws.Write(repo, "Folder/deeper/new.txt", "new file in a new folder");
        return (repo, committed);
    }
}
