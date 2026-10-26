using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

[Collection(EngineCollections.A)]
public sealed class DeleteRepoTests : IDisposable
{
    private readonly EngineWorkspace _ws = new(options => options with { DeleteRetryDelays = [TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(20)] });

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    [Fact]
    public async Task Delete_removes_the_git_directory_and_the_marker_and_leaves_the_files()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "save.dat", "progress");

        await _ws.Engine.DeleteRepoAsync(repo, Ct);

        Assert.False(Directory.Exists(repo.GitDir));
        Assert.False(File.Exists(EngineWorkspace.MarkerPath(repo)));
        Assert.Equal("progress", await File.ReadAllTextAsync(_ws.FileOf(repo, "save.dat"), Ct));
    }

    [Fact]
    public async Task A_repository_whose_damaged_index_was_set_aside_can_still_be_deleted()
    {
        var (repo, _) = await _ws.ThreePointsRepoAsync();
        await File.WriteAllBytesAsync(Path.Combine(repo.GitDir, "index"), [], Ct);
        await _ws.Engine.ForGameLockHolder().RepairIndexIfDamagedAsync(repo, Ct);
        Assert.Single(Directory.GetFiles(repo.GitDir, "*damaged*"));

        await _ws.Engine.DeleteRepoAsync(repo, Ct);

        Assert.False(Directory.Exists(repo.GitDir));
    }

    // A killed process can leave the file a classification keeps beside its index; the git directory it leaves is still SavePoint's own.
    [Fact]
    public async Task What_a_stopped_classification_leaves_beside_its_index_does_not_make_the_git_directory_look_foreign()
    {
        var repo = await _ws.InitializedRepoAsync();
        var index = Path.Combine(repo.GitDir, GitEngine.ClassifyIndexPrefix + "0123456789abcdef.index");
        await File.WriteAllTextAsync(GitEngine.ExcludeFileOf(index), string.Empty, Ct);

        await _ws.Engine.DeleteRepoAsync(repo, Ct);

        Assert.False(Directory.Exists(repo.GitDir));
    }

    [Fact]
    public async Task After_a_delete_another_game_can_claim_the_folder()
    {
        var repo = await _ws.InitializedRepoAsync();
        await _ws.Engine.DeleteRepoAsync(repo, Ct);
        var next = EngineWorkspace.Claim(repo, "game-2", RepoRole.External);

        await _ws.Engine.InitAsync(next, ct: Ct);

        Assert.Equal("game-2", EngineWorkspace.RequireMarker(next).GameId);
    }

    [Fact]
    public async Task Delete_refuses_a_folder_another_game_holds_and_removes_nothing()
    {
        var holder = await _ws.InitializedRepoAsync("other-game");
        var intruder = EngineWorkspace.Claim(holder, "game-1", RepoRole.Install);
        await CopyDirectoryAsync(holder.GitDir, intruder.GitDir);

        var error = await Assert.ThrowsAsync<ConsentMarkerException>(() => _ws.Engine.DeleteRepoAsync(intruder, Ct));

        Assert.Equal("error.consentMarker.conflict", error.MessageId);
        Assert.True(Directory.Exists(intruder.GitDir));
        Assert.Equal("other-game", EngineWorkspace.RequireMarker(holder).GameId);
    }

    [Fact]
    public async Task Delete_still_removes_the_git_directory_when_the_marker_is_already_gone()
    {
        var repo = await _ws.InitializedRepoAsync();
        File.Delete(EngineWorkspace.MarkerPath(repo));

        await _ws.Engine.DeleteRepoAsync(repo, Ct);

        Assert.False(Directory.Exists(repo.GitDir));
    }

    [Fact]
    public async Task Delete_can_be_repeated_after_the_git_directory_went_but_the_marker_stayed()
    {
        var repo = await _ws.InitializedRepoAsync();
        GitWorkspace.DeleteDirectory(repo.GitDir);

        await _ws.Engine.DeleteRepoAsync(repo, Ct);

        Assert.False(File.Exists(EngineWorkspace.MarkerPath(repo)));
    }

    [Fact]
    public async Task Delete_with_nothing_left_to_delete_succeeds()
    {
        var repo = _ws.NewRepo();

        await _ws.Engine.DeleteRepoAsync(repo, Ct);

        Assert.False(Directory.Exists(repo.GitDir));
    }

    [Fact]
    public async Task Delete_leaves_a_marker_it_cannot_read_and_still_removes_the_history()
    {
        var repo = await _ws.InitializedRepoAsync();
        EngineWorkspace.CorruptMarker(repo);

        await _ws.Engine.DeleteRepoAsync(repo, Ct);

        Assert.False(Directory.Exists(repo.GitDir));
        Assert.True(File.Exists(EngineWorkspace.MarkerPath(repo)));
    }

    [Fact]
    public async Task Delete_removes_a_git_directory_whose_pack_files_are_read_only()
    {
        var repo = await _ws.InitializedRepoAsync();
        var pack = Path.Combine(repo.GitDir, "objects", "pack", "pack-test.pack");
        Directory.CreateDirectory(Path.GetDirectoryName(pack)!);
        await File.WriteAllTextAsync(pack, "x", Ct);
        File.SetAttributes(pack, FileAttributes.ReadOnly);

        await _ws.Engine.DeleteRepoAsync(repo, Ct);

        Assert.False(Directory.Exists(repo.GitDir));
    }

    [Fact]
    public async Task Delete_removes_the_git_directory_before_the_marker()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows() && Environment.IsPrivilegedProcess, "a privileged user ignores folder permissions");
        var repo = await _ws.InitializedRepoAsync();
        using var block = BlockDeletionIn(repo.GitDir);

        await Assert.ThrowsAnyAsync<SavepointException>(() => _ws.Engine.DeleteRepoAsync(repo, Ct));

        Assert.True(File.Exists(EngineWorkspace.MarkerPath(repo)), "the marker must outlive a failed delete so the delete can be repeated");

        block.Dispose();
        await _ws.Engine.DeleteRepoAsync(repo, Ct);
        Assert.False(Directory.Exists(repo.GitDir));
        Assert.False(File.Exists(EngineWorkspace.MarkerPath(repo)));
    }

    [Fact]
    public async Task Delete_refuses_a_folder_that_is_not_a_git_directory()
    {
        var repo = await _ws.InitializedRepoAsync();
        var precious = Path.Combine(_ws.Root, "precious");
        Directory.CreateDirectory(precious);
        await File.WriteAllTextAsync(Path.Combine(precious, "photo.jpg"), "x", Ct);

        await Assert.ThrowsAsync<GitOperationException>(() => _ws.Engine.DeleteRepoAsync(repo with { GitDir = precious }, Ct));

        Assert.True(File.Exists(Path.Combine(precious, "photo.jpg")));
        Assert.True(File.Exists(EngineWorkspace.MarkerPath(repo)));
    }

    // A game folder somebody else could point a delete at: it holds one entry a git directory also has, next to the player's file.
    [Theory]
    [InlineData("logs", true)]
    [InlineData("config", true)]
    [InlineData("hooks", true)]
    [InlineData("x.lock", false)]
    public async Task Delete_refuses_a_folder_in_the_storage_root_that_holds_a_player_s_file_next_to_a_git_looking_entry(string entry, bool folder)
    {
        var repo = await _ws.InitializedRepoAsync();
        var other = Path.Combine(_ws.StorageRoot, "other-game");
        if (folder)
        {
            Directory.CreateDirectory(Path.Combine(other, entry));
        }
        else
        {
            Directory.CreateDirectory(other);
            await File.WriteAllTextAsync(Path.Combine(other, entry), string.Empty, Ct);
        }

        await File.WriteAllTextAsync(Path.Combine(other, "save1.sav"), "progress", Ct);

        await Assert.ThrowsAsync<GitOperationException>(() => _ws.Engine.DeleteRepoAsync(repo with { GitDir = other }, Ct));

        Assert.Equal("progress", await File.ReadAllTextAsync(Path.Combine(other, "save1.sav"), Ct));
        Assert.True(File.Exists(EngineWorkspace.MarkerPath(repo)), "a refused delete leaves the marker too");
    }

    [Fact]
    public async Task Delete_refuses_a_real_git_directory_outside_the_storage_root_and_removes_nothing()
    {
        var repo = await _ws.InitializedRepoAsync();
        var outside = Path.Combine(_ws.GamesRoot, "elsewhere.git");
        CopyDirectory(repo.GitDir, outside);

        await Assert.ThrowsAsync<GitOperationException>(() => _ws.Engine.DeleteRepoAsync(repo with { GitDir = outside }, Ct));

        Assert.True(File.Exists(Path.Combine(outside, "HEAD")));
        Assert.True(File.Exists(Path.Combine(repo.GitDir, "HEAD")));
        Assert.True(File.Exists(EngineWorkspace.MarkerPath(repo)));
    }

    [Fact]
    public async Task Delete_refuses_the_storage_root_itself_and_a_folder_beside_it_with_a_similar_name()
    {
        var repo = await _ws.InitializedRepoAsync();
        var lookalike = _ws.StorageRoot + "-old";
        CopyDirectory(repo.GitDir, lookalike);

        await Assert.ThrowsAsync<GitOperationException>(() => _ws.Engine.DeleteRepoAsync(repo with { GitDir = _ws.StorageRoot }, Ct));
        await Assert.ThrowsAsync<GitOperationException>(() => _ws.Engine.DeleteRepoAsync(repo with { GitDir = lookalike }, Ct));

        Assert.True(Directory.Exists(_ws.StorageRoot));
        Assert.True(File.Exists(Path.Combine(lookalike, "HEAD")));
        Assert.True(File.Exists(Path.Combine(repo.GitDir, "HEAD")));
    }

    [Fact]
    public async Task Delete_refuses_a_git_directory_inside_the_storage_root_that_SavePoint_did_not_make()
    {
        var repo = await _ws.InitializedRepoAsync();
        var foreign = Path.Combine(_ws.StorageRoot, "someone-elses.git");
        CopyDirectory(repo.GitDir, foreign);
        var config = Path.Combine(foreign, "config");
        await File.WriteAllLinesAsync(config, (await File.ReadAllLinesAsync(config, Ct)).Where(line => !line.Contains("repoConfigVersion", StringComparison.Ordinal)), Ct);

        var refused = await Assert.ThrowsAsync<GitOperationException>(() => _ws.Engine.DeleteRepoAsync(repo with { GitDir = foreign }, Ct));

        Assert.Equal("error.gitOperation.failed", refused.MessageId);
        Assert.True(File.Exists(Path.Combine(foreign, "HEAD")));
    }

    [Fact]
    public async Task Delete_finishes_a_git_directory_an_interrupted_delete_left_half_removed()
    {
        var repo = await _ws.InitializedRepoAsync();
        File.Delete(Path.Combine(repo.GitDir, "config"));
        File.Delete(Path.Combine(repo.GitDir, "HEAD"));

        await _ws.Engine.DeleteRepoAsync(repo, Ct);

        Assert.False(Directory.Exists(repo.GitDir));
        Assert.False(File.Exists(EngineWorkspace.MarkerPath(repo)));
    }

    [Fact]
    public async Task Delete_refuses_a_git_directory_that_is_a_link_to_somewhere_outside_the_storage_root()
    {
        var repo = await _ws.InitializedRepoAsync();
        var outside = Path.Combine(_ws.GamesRoot, "linked-target.git");
        CopyDirectory(repo.GitDir, outside);
        var link = Path.Combine(_ws.StorageRoot, "game-1", "linked.git");
        Assert.SkipUnless(TestLinks.TryCreateFolderLink(link, outside), "this system cannot make folder links");

        await Assert.ThrowsAsync<GitOperationException>(() => _ws.Engine.DeleteRepoAsync(repo with { GitDir = link }, Ct));

        Assert.True(File.Exists(Path.Combine(outside, "HEAD")));
        TestLinks.RemoveFolderLink(link);
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)));
        }
    }

    [Fact]
    public async Task Delete_refuses_a_protected_work_tree()
    {
        var repo = await _ws.InitializedRepoAsync();
        var protectedRepo = repo with { WorkTree = _ws.Platform.Directories.HomeDir };
        Directory.CreateDirectory(protectedRepo.WorkTree);

        await Assert.ThrowsAsync<PathSafetyException>(() => _ws.Engine.DeleteRepoAsync(protectedRepo, Ct));

        Assert.True(Directory.Exists(repo.GitDir));
    }

    [Fact]
    public async Task Consent_is_not_confirmed_before_init_and_is_confirmed_after_it()
    {
        var repo = _ws.NewRepo();
        Assert.False(_ws.Engine.GetConsentStatus(repo).Confirmed);

        await _ws.Engine.InitAsync(repo, ct: Ct);

        var status = _ws.Engine.GetConsentStatus(repo);
        Assert.True(status.Confirmed);
        Assert.NotNull(status.ConfirmedAt);
    }

    [Fact]
    public async Task Consent_held_by_another_game_is_not_confirmation_for_this_one()
    {
        var holder = await _ws.InitializedRepoAsync("other-game");

        var status = _ws.Engine.GetConsentStatus(EngineWorkspace.Claim(holder, "game-1", RepoRole.Install));

        Assert.False(status.Confirmed);
        Assert.Null(status.ConfirmedAt);
    }

    [Fact]
    public void Consent_status_never_throws_for_an_unreadable_marker_or_a_missing_folder()
    {
        var repo = _ws.NewRepo();
        EngineWorkspace.CorruptMarker(repo);

        Assert.False(_ws.Engine.GetConsentStatus(repo).Confirmed);
        Assert.False(_ws.Engine.GetConsentStatus(repo with { WorkTree = Path.Combine(_ws.Root, "nowhere") }).Confirmed);
    }

    [Fact]
    public async Task A_delete_stopped_by_a_program_holding_a_file_names_the_file_as_in_use()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows refuses to delete a file another program holds open");
        var repo = await _ws.InitializedRepoAsync();
        using var block = BlockDeletionIn(repo.GitDir);

        var error = await Assert.ThrowsAsync<GitOperationException>(() => _ws.Engine.DeleteRepoAsync(repo, Ct));

        Assert.Equal("error.gitOperation.checkoutFailed", error.MessageId);
        Assert.StartsWith(repo.GitDir, (string)error.Args["path"]!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_delete_stopped_by_denied_access_is_not_reported_as_a_file_in_use()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows() && Environment.IsPrivilegedProcess, "a privileged user ignores folder permissions");
        var repo = await _ws.InitializedRepoAsync();
        using var denied = DenyDeletionIn(repo.GitDir);

        var error = await Assert.ThrowsAsync<StorageRemoveFailedException>(() => _ws.Engine.DeleteRepoAsync(repo, Ct));

        Assert.Equal("error.storage.removeFailed", error.MessageId);
        Assert.StartsWith(repo.GitDir, (string)error.Args["path"]!, StringComparison.Ordinal);
    }

    // Makes removing something inside the folder fail with "access denied" until the returned object is disposed.
    private static IDisposable DenyDeletionIn(string gitDir) =>
        OperatingSystem.IsWindows() ? DenyDeletingOnWindows(gitDir) : MakeReadOnlyOnUnix(Path.Combine(gitDir, "refs"));

    [SupportedOSPlatform("windows")]
    private static DelegateDisposable DenyDeletingOnWindows(string gitDir)
    {
        var user = WindowsIdentity.GetCurrent().User!;
        var head = new FileInfo(Path.Combine(gitDir, "HEAD"));
        var folder = new DirectoryInfo(gitDir);
        var fileRule = new FileSystemAccessRule(user, FileSystemRights.Delete, AccessControlType.Deny);
        var folderRule = new FileSystemAccessRule(user, FileSystemRights.DeleteSubdirectoriesAndFiles, AccessControlType.Deny);
        var fileSecurity = head.GetAccessControl();
        fileSecurity.AddAccessRule(fileRule);
        head.SetAccessControl(fileSecurity);
        var folderSecurity = folder.GetAccessControl();
        folderSecurity.AddAccessRule(folderRule);
        folder.SetAccessControl(folderSecurity);
        return new DelegateDisposable(() =>
        {
            var restoredFolder = folder.GetAccessControl();
            restoredFolder.RemoveAccessRule(folderRule);
            folder.SetAccessControl(restoredFolder);
            var restoredFile = head.GetAccessControl();
            restoredFile.RemoveAccessRule(fileRule);
            head.SetAccessControl(restoredFile);
        });
    }

    // Makes removing something inside the folder fail until the returned object is disposed.
    private static IDisposable BlockDeletionIn(string gitDir)
    {
        Assert.True(File.Exists(Path.Combine(gitDir, "HEAD")), $"expected a repository at {gitDir}");
        if (OperatingSystem.IsWindows())
        {
            var held = Path.Combine(gitDir, "HEAD");
            return new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.None);
        }

        return MakeReadOnlyOnUnix(Path.Combine(gitDir, "refs"));
    }

    [UnsupportedOSPlatform("windows")]
    private static IDisposable MakeReadOnlyOnUnix(string folder)
    {
        File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        var restore = new DelegateDisposable(() =>
        {
            // The folder is gone when the test got as far as a delete that worked.
            if (Directory.Exists(folder))
            {
                File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        });

        // A mount that keeps no permissions (Windows drives under WSL) lets the write through.
        var probe = Path.Combine(folder, "permission-probe");
        try
        {
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            restore.Dispose();
            Assert.Skip("this file system ignores folder permissions");
        }
        catch (UnauthorizedAccessException)
        {
        }

        return restore;
    }

    private static async Task CopyDirectoryAsync(string source, string target)
    {
        Assert.True(Directory.Exists(source), $"expected the engine to have created {source}");
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        }

        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var from = File.OpenRead(file);
            await using var to = File.Create(destination);
            await from.CopyToAsync(to, TestContext.Current.CancellationToken);
        }
    }

    private sealed class DelegateDisposable(Action action) : IDisposable
    {
        private Action? _action = action;

        public void Dispose()
        {
            _action?.Invoke();
            _action = null;
        }
    }
}
