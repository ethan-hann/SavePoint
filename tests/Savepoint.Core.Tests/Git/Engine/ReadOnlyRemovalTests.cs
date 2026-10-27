using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// Windows refuses to delete a read-only file, where git's own replacing of a path clears the attribute and goes on. Every file SavePoint
// removes itself clears the attribute first, so a read-only file that is SavePoint's to remove does not stop a switch, a clean or a discard.
[Collection(EngineCollections.D)]
public sealed class ReadOnlyRemovalTests : IDisposable
{
    private readonly EngineWorkspace _ws;

    public ReadOnlyRemovalTests()
    {
        _ws = new(options => options with
        {
            ResyncDelays = [TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(20)],
        });
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    private static void MakeReadOnly(string file) => File.SetAttributes(file, File.GetAttributes(file) | FileAttributes.ReadOnly);

    // profile-a and profile-b differ in a-only.txt and b-only.txt; the folder is on profile-b.
    private Task<(RepoRef Repo, string Unused)> TwoProfilesAsync() =>
        RepoTemplates.CopyAsync("read-only-removal-two-profiles", _ws, async ws =>
        {
            var repo = await ws.InitializedFreshAsync();
            ws.Write(repo, "shared.txt", "same in both");
            ws.Write(repo, "a-only.txt", "a");
            await ws.CaptureAsync(repo, "profile-a", "A");
            await ws.Engine.CreateBranchAsync(repo, "profile-b", "profile-a", TestContext.Current.CancellationToken);
            await ws.Engine.CheckoutBranchAsync(repo, "profile-b", ct: TestContext.Current.CancellationToken);
            ws.Delete(repo, "a-only.txt");
            ws.Write(repo, "b-only.txt", "b");
            await ws.CaptureAsync(repo, "profile-b", "B");
            return (repo, string.Empty);
        });

    [Fact]
    public async Task A_read_only_file_the_old_point_held_that_comes_back_while_a_switch_is_verified_is_removed()
    {
        var repo = (await TwoProfilesAsync()).Repo;
        var made = false;
        _ws.OnInvoke = invocation =>
        {
            if (!made && EngineWorkspace.IsVerifyDiff(invocation))
            {
                made = true;
                File.WriteAllText(_ws.FileOf(repo, "b-only.txt"), "b");
                MakeReadOnly(_ws.FileOf(repo, "b-only.txt"));
            }
        };

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);

        Assert.True(made, "the file never came back, so the test did not reach the step it is about");
        Assert.False(File.Exists(_ws.FileOf(repo, "b-only.txt")));
        Assert.Equal("a", File.ReadAllText(_ws.FileOf(repo, "a-only.txt")));
    }

    [Fact]
    public async Task A_read_only_new_file_that_appears_as_a_clean_ends_is_removed()
    {
        var (repo, _) = await _ws.ThreePointsRepoAsync();
        _ws.Write(repo, "new.txt", "a new file");
        var cleaned = false;
        var made = false;
        _ws.OnInvoke = invocation =>
        {
            if (invocation.Arguments[0] == "clean")
            {
                cleaned = true;
            }
            else if (cleaned && !made && invocation.ReadOnly)
            {
                made = true;
                File.WriteAllText(_ws.FileOf(repo, "late.txt"), "made as the clean ended");
                MakeReadOnly(_ws.FileOf(repo, "late.txt"));
            }
        };

        await _ws.Engine.CleanAsync(repo, Ct);

        Assert.True(made, "no file appeared after the clean, so the test did not reach the step it is about");
        Assert.False(File.Exists(_ws.FileOf(repo, "late.txt")));
        Assert.False(File.Exists(_ws.FileOf(repo, "new.txt")));
    }

    // profile-a holds a file named logs, profile-b holds logs/run.txt; the patterns name the folder, so the switch to profile-b removes the
    // player's old file itself, without git. The folder is on profile-a.
    private async Task<RepoRef> LogsAsync()
    {
        var (repo, _) = await RepoTemplates.CopyAsync<string?>("read-only-removal-logs", _ws, async ws =>
        {
            var repo = await ws.InitializedFreshAsync();
            ws.Write(repo, "logs", "a's logs file");
            ws.Write(repo, "a-only.txt", "a");
            await ws.CaptureAsync(repo, "profile-a", "A");
            await ws.Engine.CreateBranchAsync(repo, "profile-b", "profile-a", TestContext.Current.CancellationToken);
            await ws.Engine.CheckoutBranchAsync(repo, "profile-b", ct: TestContext.Current.CancellationToken);
            ws.Delete(repo, "logs");
            ws.Delete(repo, "a-only.txt");
            ws.Write(repo, "logs/run.txt", "b's run log");
            ws.Write(repo, "b-only.txt", "b");
            await ws.CaptureAsync(repo, "profile-b", "B");
            await ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: TestContext.Current.CancellationToken);
            return (repo, null);
        });
        return repo with { IgnorePatterns = ["logs/"] };
    }

    [Fact]
    public async Task A_read_only_file_standing_where_the_switch_needs_a_folder_is_removed_by_the_switch()
    {
        var repo = await LogsAsync();
        MakeReadOnly(_ws.FileOf(repo, "logs"));

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-b", ct: Ct);

        Assert.Equal("refs/heads/profile-b", await _ws.HeadRefAsync(repo));
        Assert.False(File.Exists(_ws.FileOf(repo, "logs")));
        Assert.False(File.Exists(_ws.FileOf(repo, "a-only.txt")));
    }

    [Fact]
    public async Task A_read_only_file_that_another_program_holds_keeps_its_mark_when_the_finish_cannot_remove_it()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows refuses to remove a file another program holds open, or one that is read-only");
        var (repo, data) = await FileAndFolderProfilesAsync();
        SwitchJournal.Write(repo.GitDir, data.B, data.A, "profile-a");
        _ws.Delete(repo, "Mods/Foo/x.txt");
        Directory.Delete(_ws.FileOf(repo, "Mods/Foo"));
        var standing = _ws.Write(repo, "Mods/Foo", "a's file, cut sh");
        MakeReadOnly(standing);

        // Open for reading with every kind of sharing but delete: the mark can be changed and the file cannot be removed.
        await using (new FileStream(standing, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            await Assert.ThrowsAnyAsync<Savepoint.Core.Errors.SavepointException>(() => _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct));
        }

        Assert.True((File.GetAttributes(standing) & FileAttributes.ReadOnly) != 0, "the file lost its read-only mark though it was not removed");
        Assert.Equal("a's file, cut sh", File.ReadAllText(standing));
    }

    // Profile-a holds Mods/Foo as a file, profile-b as a folder holding x.txt. The folder is on profile-b.
    private async Task<(RepoRef Repo, (string A, string B) Data)> FileAndFolderProfilesAsync() =>
        await RepoTemplates.CopyAsync("read-only-removal-file-folder", _ws, async ws =>
        {
            var repo = await ws.InitializedFreshAsync();
            ws.Write(repo, "Mods/Foo", "a's file");
            var a = (await ws.CaptureAsync(repo, "profile-a", "A")).TipCommitId!;
            await ws.Engine.CreateBranchAsync(repo, "profile-b", "profile-a", TestContext.Current.CancellationToken);
            await ws.Engine.CheckoutBranchAsync(repo, "profile-b", ct: TestContext.Current.CancellationToken);
            ws.Delete(repo, "Mods/Foo");
            ws.Write(repo, "Mods/Foo/x.txt", "b's x");
            var b = (await ws.CaptureAsync(repo, "profile-b", "B")).TipCommitId!;
            return (repo, (a, b));
        });
}
