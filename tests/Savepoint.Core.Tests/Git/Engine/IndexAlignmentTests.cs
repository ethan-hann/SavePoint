using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// A switch makes the index agree with the target for the files the game's patterns name, so git sees nothing to do for them. That works
// on a copy of the index: an entry the alignment drops that is clean profile content the target lacks is removed with the switch,
// and any refusal puts the index and those files back exactly as they were.
[Collection(EngineCollections.D)]
public sealed class IndexAlignmentTests : IDisposable
{
    private readonly EngineWorkspace _ws = new(options => options with
    {
        ResyncDelays = [TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(20)],
        LockRetryDelays = [TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10)],
    });

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    // profile-a holds a file named logs, profile-b holds logs/run.txt (recorded before the pattern "logs/" was added); a-only.txt and
    // b-only.txt tell the switches apart. The folder is on profile-a. The patterns name the folder, not the file.
    private async Task<RepoRef> LogsAsync()
    {
        var (repo, _) = await RepoTemplates.CopyAsync<string?>("index-alignment-logs", _ws, async ws =>
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

    // What the index records for every entry (mode, id, stage, skip-worktree tag, path), not the bytes of its file.
    private async Task<string> IndexAsync(RepoRef repo) =>
        string.Join('\n', (await _ws.GitAsync(repo, "ls-files", "-s", "-t", "-z")).SplitStandardOutputOnNul());

    // The copies the alignment works on are the engine's own, and none stays in the git directory once a switch has ended.
    private static void AssertNoAlignmentFiles(RepoRef repo) => Assert.Empty(Directory.EnumerateFiles(repo.GitDir, "savepoint-align-*"));

    private static string OnDisk(RepoRef repo, string path) => Path.Combine(repo.WorkTree, path.Replace('/', Path.DirectorySeparatorChar));

    // The folder the player's file `logs` is in the way of: the pattern names the folder only, so the switch to profile-b has no file to
    // write there, and the file the point itself holds goes with the switch.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_file_the_target_replaces_with_a_folder_the_patterns_name_goes_with_the_switch_and_nothing_is_written_in_its_place(bool force)
    {
        var repo = await LogsAsync();

        if (force)
        {
            await _ws.Engine.ForceCheckoutBranchAsync(repo, "profile-b", Ct);
        }
        else
        {
            await _ws.Engine.CheckoutBranchAsync(repo, "profile-b", ct: Ct);
        }

        Assert.Equal("refs/heads/profile-b", await _ws.HeadRefAsync(repo));
        Assert.False(File.Exists(OnDisk(repo, "logs")), "profile-a's file logs is still there");
        Assert.False(Directory.Exists(OnDisk(repo, "logs")), "a folder logs was made, which the patterns name");
        Assert.False(File.Exists(OnDisk(repo, "a-only.txt")));
        Assert.True(File.Exists(OnDisk(repo, "b-only.txt")));
        var status = await _ws.Engine.GetStatusAsync(repo, Ct);
        Assert.True(status.IsClean, "changed: " + string.Join(", ", status.ChangedPaths) + "; new: " + string.Join(", ", status.UntrackedPaths));
        Assert.Null(status.InterruptedSwitch);
        Assert.False(File.Exists(SwitchJournal.PathIn(repo.GitDir)));
        AssertNoAlignmentFiles(repo);
    }

    // Going the other way the folder is the player's: profile-a's file cannot be written over it, and the switch says so instead of
    // deleting what is inside.
    [Fact]
    public async Task The_way_back_to_the_file_is_refused_and_leaves_the_index_and_the_folder_as_they_were()
    {
        var repo = await LogsAsync();
        await _ws.Engine.CheckoutBranchAsync(repo with { IgnorePatterns = [] }, "profile-b", ct: Ct);
        _ws.Write(repo, "logs/player.txt", "the player's own file");
        var before = await IndexAsync(repo);

        var thrown = await Record.ExceptionAsync(() => _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct));

        Assert.Equal("the player's own file", await File.ReadAllTextAsync(OnDisk(repo, "logs/player.txt"), Ct));
        Assert.Equal("error.launch.fileFolderClash", Assert.IsAssignableFrom<SavepointException>(thrown).MessageId);
        Assert.Equal(before, await IndexAsync(repo));
        Assert.Equal("refs/heads/profile-b", await _ws.HeadRefAsync(repo));
        var status = await _ws.Engine.GetStatusAsync(repo, Ct);
        Assert.True(status.IsClean);
        Assert.Contains("logs/run.txt", before);
    }

    // git refuses at the last moment (a file the switch would overwrite was edited after the check): everything goes back, the file that
    // went with the switch included, and the index records what it did before.
    [Fact]
    public async Task A_refusal_by_git_puts_back_the_index_and_the_file_that_was_removed_for_the_switch()
    {
        var repo = await LogsAsync();
        var before = await IndexAsync(repo);
        _ws.OnInvoke = invocation =>
        {
            if (invocation.Arguments[0] == "checkout")
            {
                _ws.Write(repo, "a-only.txt", "edited at the last moment");
            }
        };

        await Assert.ThrowsAsync<DirtyWorkingTreeException>(() => _ws.Engine.CheckoutBranchAsync(repo, "profile-b", ct: Ct));

        _ws.OnInvoke = null;
        Assert.Equal("a's logs file", await File.ReadAllTextAsync(OnDisk(repo, "logs"), Ct));
        Assert.Equal(before, await IndexAsync(repo));
        Assert.Equal("refs/heads/profile-a", await _ws.HeadRefAsync(repo));
        Assert.False(File.Exists(SwitchJournal.PathIn(repo.GitDir)));
        AssertNoAlignmentFiles(repo);
        var status = await _ws.Engine.GetStatusAsync(repo, Ct);
        Assert.Equal(["a-only.txt"], status.ChangedPaths);
        Assert.Empty(status.UntrackedPaths);
        Assert.Null(status.InterruptedSwitch);
    }

    // git's lock is taken by another program as the alignment starts and stays held through every retry: the same refusal.
    [Fact]
    public async Task A_lock_that_stays_held_leaves_the_index_and_the_file_and_no_note()
    {
        var repo = await LogsAsync();
        var before = await IndexAsync(repo);
        var gate = Path.Combine(repo.GitDir, "index.lock");
        _ws.OnInvoke = invocation =>
        {
            if (EngineWorkspace.IsIndexAlignment(invocation))
            {
                File.WriteAllText(gate, string.Empty);
            }
        };

        await Assert.ThrowsAsync<GitOperationException>(() => _ws.Engine.CheckoutBranchAsync(repo, "profile-b", ct: Ct));

        _ws.OnInvoke = null;
        File.Delete(gate);
        Assert.Equal("a's logs file", await File.ReadAllTextAsync(OnDisk(repo, "logs"), Ct));
        Assert.Equal(before, await IndexAsync(repo));
        Assert.False(File.Exists(SwitchJournal.PathIn(repo.GitDir)));
        AssertNoAlignmentFiles(repo);
        var status = await _ws.Engine.GetStatusAsync(repo, Ct);
        Assert.True(status.IsClean);
        Assert.Null(status.InterruptedSwitch);
    }

    // An edit to the file between the check and the removal is the player's work: the switch is refused and the edit stays.
    [Fact]
    public async Task A_file_edited_after_the_check_is_not_removed_with_the_switch()
    {
        var repo = await LogsAsync();
        var before = await IndexAsync(repo);
        var armed = true;
        _ws.OnInvoke = invocation =>
        {
            if (armed && EngineWorkspace.IsIndexAlignment(invocation))
            {
                armed = false;
                _ws.Write(repo, "logs", "the player's edit");
            }
        };

        await Assert.ThrowsAsync<DirtyWorkingTreeException>(() => _ws.Engine.CheckoutBranchAsync(repo, "profile-b", ct: Ct));

        _ws.OnInvoke = null;
        Assert.Equal("the player's edit", await File.ReadAllTextAsync(OnDisk(repo, "logs"), Ct));
        Assert.Equal(before, await IndexAsync(repo));
        Assert.False(File.Exists(SwitchJournal.PathIn(repo.GitDir)));
        AssertNoAlignmentFiles(repo);
        Assert.Equal(["logs"], (await _ws.Engine.GetStatusAsync(repo, Ct)).ChangedPaths);
    }

    // Every write of the aligned index fails, as it does when git's lock stays held: the switch is refused whole, with no note and a clean folder.
    [Fact]
    public async Task A_switch_whose_every_alignment_write_fails_leaves_no_note_and_a_clean_folder()
    {
        var repo = await LogsAsync();
        var before = await IndexAsync(repo);
        _ws.OnInvoke = invocation =>
        {
            if (EngineWorkspace.IsIndexAlignment(invocation))
            {
                throw new GitOperationException("the index lock stayed held");
            }
        };

        await Assert.ThrowsAsync<GitOperationException>(() => _ws.Engine.CheckoutBranchAsync(repo, "profile-b", ct: Ct));

        _ws.OnInvoke = null;
        Assert.False(File.Exists(SwitchJournal.PathIn(repo.GitDir)));
        AssertNoAlignmentFiles(repo);
        Assert.Equal("a's logs file", await File.ReadAllTextAsync(OnDisk(repo, "logs"), Ct));
        Assert.Equal(before, await IndexAsync(repo));
        var status = await _ws.Engine.GetStatusAsync(repo, Ct);
        Assert.True(status.IsClean);
        Assert.Null(status.InterruptedSwitch);
    }

    // A switch stopped at git's checkout is an ordinary stopped switch: repeating it finishes, and so does a discard, which brings the
    // file back.
    [Theory]
    [InlineData("repeat")]
    [InlineData("discard")]
    public async Task A_switch_stopped_after_the_file_was_removed_is_finished_or_discarded(string then)
    {
        var repo = await LogsAsync();
        _ws.OnInvoke = invocation =>
        {
            if (invocation.Arguments[0] == "checkout")
            {
                throw new InvalidOperationException("the switch was stopped");
            }
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => _ws.Engine.CheckoutBranchAsync(repo, "profile-b", ct: Ct));
        _ws.OnInvoke = null;
        Assert.NotNull(SwitchJournal.Read(repo.GitDir));

        if (then == "repeat")
        {
            await _ws.Engine.CheckoutBranchAsync(repo, "profile-b", ct: Ct);
            Assert.Equal("refs/heads/profile-b", await _ws.HeadRefAsync(repo));
            Assert.False(File.Exists(OnDisk(repo, "logs")));
            Assert.True(File.Exists(OnDisk(repo, "b-only.txt")));
        }
        else
        {
            await _ws.Engine.DiscardChangesAsync(repo, Ct);
            Assert.Equal("refs/heads/profile-a", await _ws.HeadRefAsync(repo));
            Assert.Equal("a's logs file", await File.ReadAllTextAsync(OnDisk(repo, "logs"), Ct));
        }

        Assert.Null(SwitchJournal.Read(repo.GitDir));
        Assert.True((await _ws.Engine.GetStatusAsync(repo, Ct)).IsClean);
    }
}
