using Savepoint.Core.Errors;
using Savepoint.Core.Git;
using Savepoint.Testing;

namespace Savepoint.Core.Tests.Git.Engine;

// A power loss right after a snapshot or switch can leave git's index file empty, short or zero-filled, and every git command then
// fails. SavePoint sets the file aside and rebuilds it from the latest snapshot, which never touches the game's files.
[Collection(EngineCollections.F)]
public sealed class IndexRepairTests : IDisposable
{
    private const string _damagedId = "error.gitOperation.recordDamaged";

    private readonly EngineWorkspace _ws = new();
    private readonly GitEngine _locked;

    public IndexRepairTests() => _locked = _ws.Engine.ForGameLockHolder();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    // The work that holds the game's lock is the only work that repairs.
    private Task<CaptureOutcome> CaptureAsync(RepoRef repo, string message) =>
        _locked.CaptureAsync(repo, new CaptureRequest("profile-1", message, Guid.NewGuid().ToString()) { IgnorePatterns = [] }, null, Ct);

    private static string IndexOf(RepoRef repo) => Path.Combine(repo.GitDir, "index");

    // The name ends in .tmp so that deleting the repository accepts it, and has no prefix the sweep of stale files knows.
    private static string[] Backups(RepoRef repo) => Directory.GetFiles(repo.GitDir, "savepoint-damaged-index-*.tmp");

    private static void Empty(RepoRef repo) => File.WriteAllBytes(IndexOf(repo), []);

    private static void ZeroFill(RepoRef repo) => File.WriteAllBytes(IndexOf(repo), new byte[new FileInfo(IndexOf(repo)).Length]);

    private static void Truncate(RepoRef repo) => File.WriteAllBytes(IndexOf(repo), File.ReadAllBytes(IndexOf(repo))[..20]);

    // The loose object a snapshot is stored as, removed the way a lost write would.
    private async Task RemoveObjectAsync(RepoRef repo, string id)
    {
        var path = Path.Combine(repo.GitDir, "objects", id[..2], id[2..]);
        Assert.True(File.Exists(path), "expected the snapshot to be stored as a loose object");
        File.SetAttributes(path, FileAttributes.Normal);
        File.Delete(path);
        Assert.False(await _ws.ObjectExistsAsync(repo, id));
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("zero-filled")]
    [InlineData("truncated")]
    public async Task A_snapshot_after_the_index_was_damaged_is_taken_and_the_game_files_are_untouched(string damage)
    {
        var (repo, _) = await _ws.ThreePointsRepoAsync();
        _ws.Write(repo, "c.txt", "new");
        var before = TreeSnapshot.Take(repo.WorkTree);
        Action damageIndex = damage switch
        {
            "empty" => () => Empty(repo),
            "zero-filled" => () => ZeroFill(repo),
            _ => () => Truncate(repo),
        };
        damageIndex();

        var failure = await Record.ExceptionAsync(() => CaptureAsync(repo, "After the power cut"));

        Assert.Null(failure);
        Assert.Equal(["a.txt", "b.txt", "c.txt"], (await _ws.TreeAsync(repo, "profile-1")).Order());
        Assert.Equal(before, TreeSnapshot.Take(repo.WorkTree));
        Assert.Single(Backups(repo));
    }

    [Fact]
    public async Task The_damaged_index_is_kept_under_a_new_name_beside_the_rebuilt_one()
    {
        var (repo, _) = await _ws.ThreePointsRepoAsync();
        var damaged = File.ReadAllBytes(IndexOf(repo))[..20];
        File.WriteAllBytes(IndexOf(repo), damaged);

        var failure = await Record.ExceptionAsync(() => CaptureAsync(repo, "After the power cut"));

        Assert.Null(failure);
        var backup = Assert.Single(Backups(repo));
        Assert.Equal(damaged, File.ReadAllBytes(backup));
        Assert.True(new FileInfo(IndexOf(repo)).Length > 20, "the index was rebuilt");
    }

    [Fact]
    public async Task A_switch_after_the_index_was_damaged_goes_through()
    {
        var (repo, points) = await _ws.ThreePointsRepoAsync();
        Empty(repo);

        var failure = await Record.ExceptionAsync(() => _locked.CheckoutCommitAsync(repo, points[0], ct: Ct));

        Assert.Null(failure);
        Assert.Equal("one", await File.ReadAllTextAsync(_ws.FileOf(repo, "a.txt"), Ct));
        Assert.False(File.Exists(_ws.FileOf(repo, "b.txt")));
    }

    [Fact]
    public async Task A_first_snapshot_that_was_cut_off_before_anything_was_recorded_is_taken_afresh()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "one");
        Empty(repo);
        var before = TreeSnapshot.Take(repo.WorkTree);

        var failure = await Record.ExceptionAsync(() => CaptureAsync(repo, "First"));

        Assert.Null(failure);
        Assert.Equal(["a.txt"], await _ws.TreeAsync(repo, "profile-1"));
        Assert.Equal(before, TreeSnapshot.Take(repo.WorkTree));
        Assert.Single(Backups(repo));
    }

    [Fact]
    public async Task Reading_the_status_repairs_the_index_once_even_though_it_asks_git_twice_at_the_same_time()
    {
        var (repo, _) = await _ws.ThreePointsRepoAsync();
        _ws.Write(repo, "c.txt", "new");
        Empty(repo);

        RepoStatus? status = null;
        var failure = await Record.ExceptionAsync(async () => status = await _locked.GetStatusAsync(repo, Ct));

        Assert.Null(failure);
        Assert.NotNull(status);
        Assert.False(status.IsClean);
        Assert.Single(Backups(repo));
    }

    [Fact]
    public async Task When_the_latest_snapshot_cannot_be_read_the_error_says_so_and_nothing_is_changed()
    {
        var (repo, _) = await _ws.ThreePointsRepoAsync();
        _ws.Write(repo, "c.txt", "new");
        await RemoveObjectAsync(repo, (await _ws.ResolveAsync(repo, "profile-1"))!);
        Empty(repo);
        var before = TreeSnapshot.Take(repo.WorkTree);

        var error = await Assert.ThrowsAsync<RepositoryDamagedException>(() => _locked.ListCaptureCandidatesAsync(repo, [], Ct));

        Assert.Equal(_damagedId, error.MessageId);
        Assert.Equal(before, TreeSnapshot.Take(repo.WorkTree));
        Assert.Empty(Backups(repo));
        Assert.Equal(0, new FileInfo(IndexOf(repo)).Length);
    }

    [Fact]
    public async Task A_profile_whose_record_was_emptied_is_not_mistaken_for_one_with_nothing_saved()
    {
        var (repo, _) = await _ws.ThreePointsRepoAsync();
        File.WriteAllBytes(Path.Combine(repo.GitDir, "refs", "heads", "profile-1"), []);
        Empty(repo);

        await Assert.ThrowsAsync<RepositoryDamagedException>(() => _locked.ListCaptureCandidatesAsync(repo, [], Ct));

        Assert.Empty(Backups(repo));
        Assert.Equal(0, new FileInfo(IndexOf(repo)).Length);
    }

    [Fact]
    public async Task A_profile_kept_in_the_packed_refs_has_its_index_rebuilt_from_it()
    {
        var (repo, _) = await _ws.ThreePointsRepoAsync();
        await _ws.GitWriteAsync(repo, "pack-refs", "--all", "--prune");
        Assert.False(File.Exists(Path.Combine(repo.GitDir, "refs", "heads", "profile-1")), "the ref should now be packed");
        Empty(repo);

        await _locked.ListCaptureCandidatesAsync(repo, [], Ct);

        Assert.Equal(["a.txt", "b.txt"], (await _ws.GitOutAsync(repo, "ls-files")).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Order());
    }

    [Fact]
    public async Task A_snapshot_cannot_be_taken_either_when_the_latest_one_cannot_be_read()
    {
        var (repo, _) = await _ws.ThreePointsRepoAsync();
        _ws.Write(repo, "c.txt", "new");
        await RemoveObjectAsync(repo, (await _ws.ResolveAsync(repo, "profile-1"))!);
        Empty(repo);
        var before = TreeSnapshot.Take(repo.WorkTree);

        var error = await Record.ExceptionAsync(() => CaptureAsync(repo, "After the power cut"));

        var damaged = Assert.IsType<RepositoryDamagedException>(error);
        Assert.Equal(_damagedId, damaged.MessageId);
        Assert.Equal(before, TreeSnapshot.Take(repo.WorkTree));
    }

    [Fact]
    public async Task An_index_that_is_damaged_again_right_after_the_repair_is_reported_instead_of_repaired_forever()
    {
        var (repo, _) = await _ws.ThreePointsRepoAsync();
        Empty(repo);
        var repaired = false;
        _ws.OnInvoke = invocation =>
        {
            if (invocation.Arguments[0] == "read-tree")
            {
                repaired = true;
            }
            else if (repaired)
            {
                Empty(repo);
            }
        };

        var error = await Assert.ThrowsAsync<RepositoryDamagedException>(() => _locked.GetStatusAsync(repo, Ct));

        Assert.Equal(_damagedId, error.MessageId);

        // Two reads run side by side, so each may have asked once; neither asks again.
        Assert.InRange(_ws.Invocations.Count(invocation => invocation.Arguments[0] == "read-tree"), 1, 2);
    }

    [Fact]
    public async Task A_repair_is_written_to_the_log_with_the_name_of_the_set_aside_file()
    {
        var (repo, _) = await _ws.ThreePointsRepoAsync();
        Empty(repo);
        var log = new RecordingDiagnosticLog();
        var engine = new GitEngine(new GitRunner(_ws.Binary, new GitRunnerOptions { Log = log }), _ws.PathSafety, _ws.Space.Probe, _ws.StorageRoot).ForGameLockHolder();

        await engine.GetStatusAsync(repo, Ct);

        var entry = Assert.Single(log.Entries, e => e.Message.Contains("index", StringComparison.OrdinalIgnoreCase) && e.Level != LogLevel.Error && e.Message.Contains("rebuilt", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(Path.GetFileName(Assert.Single(Backups(repo))), entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_read_made_without_the_game_lock_leaves_a_damaged_index_alone()
    {
        var (repo, _) = await _ws.ThreePointsRepoAsync();
        Empty(repo);

        var error = await Assert.ThrowsAsync<GitOperationException>(() => _ws.Engine.GetStatusAsync(repo, Ct));

        Assert.Equal("error.gitOperation.failed", error.MessageId);
        Assert.Empty(Backups(repo));
        Assert.Equal(0, new FileInfo(IndexOf(repo)).Length);
    }

    [Fact]
    public async Task The_next_operation_that_holds_the_lock_repairs_what_a_read_without_it_left()
    {
        var (repo, _) = await _ws.ThreePointsRepoAsync();
        Empty(repo);
        await Assert.ThrowsAsync<GitOperationException>(() => _ws.Engine.GetStatusAsync(repo, Ct));

        var failure = await Record.ExceptionAsync(() => _locked.GetStatusAsync(repo, Ct));

        Assert.Null(failure);
        Assert.Single(Backups(repo));
    }

    [Fact]
    public async Task Repairing_an_index_that_is_not_damaged_does_nothing()
    {
        var (repo, _) = await _ws.ThreePointsRepoAsync();
        var before = File.ReadAllBytes(IndexOf(repo));

        await _locked.RepairIndexIfDamagedAsync(repo, Ct);

        Assert.Empty(Backups(repo));
        Assert.Equal(before, File.ReadAllBytes(IndexOf(repo)));
    }

    [Fact]
    public async Task Repairing_on_request_sets_a_damaged_index_right()
    {
        var (repo, _) = await _ws.ThreePointsRepoAsync();
        Empty(repo);

        await _locked.RepairIndexIfDamagedAsync(repo, Ct);

        Assert.Single(Backups(repo));
        Assert.Equal(["a.txt", "b.txt"], (await _ws.GitOutAsync(repo, "ls-files")).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Order());
    }

    [Fact]
    public async Task Only_the_three_newest_set_aside_indexes_are_kept()
    {
        var (repo, _) = await _ws.ThreePointsRepoAsync();
        string[] old = [.. Enumerable.Range(1, 5).Select(day => $"savepoint-damaged-index-2020010{day}T000000000.tmp")];
        foreach (var name in old)
        {
            File.WriteAllBytes(Path.Combine(repo.GitDir, name), [1]);
        }

        Empty(repo);

        await _locked.GetStatusAsync(repo, Ct);

        var kept = Backups(repo).Select(Path.GetFileName).Order().ToArray();
        Assert.Equal(3, kept.Length);
        Assert.Equal(old[3], kept[0]);
        Assert.Equal(old[4], kept[1]);
        Assert.DoesNotContain(kept[2]!, old);
    }

    [Fact]
    public async Task A_repair_that_met_a_held_lock_reports_that_and_leaves_the_damaged_index_where_it_was()
    {
        var (repo, _) = await _ws.ThreePointsRepoAsync();
        Empty(repo);
        await File.WriteAllTextAsync(Path.Combine(repo.GitDir, "index.lock"), string.Empty, Ct);

        var error = await Assert.ThrowsAsync<GitOperationException>(() => _locked.GetStatusAsync(repo, Ct));

        Assert.Equal("error.gitOperation.checkoutFailed", error.MessageId);
        Assert.Empty(Backups(repo));
        Assert.Equal(0, new FileInfo(IndexOf(repo)).Length);
        Assert.Empty(Directory.GetFiles(repo.GitDir, "*.tmp"));
    }

    [Fact]
    public async Task The_index_file_is_never_missing_while_the_new_one_is_built()
    {
        var (repo, _) = await _ws.ThreePointsRepoAsync();
        Empty(repo);
        using var building = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        _ws.OnInvoke = invocation =>
        {
            if (invocation.Arguments[0] == "read-tree")
            {
                building.Set();
                release.Wait(TimeSpan.FromSeconds(60));
            }
        };

        var repair = Task.Run(() => _locked.RepairIndexIfDamagedAsync(repo, Ct), Ct);
        Assert.True(building.Wait(TimeSpan.FromSeconds(60), Ct), "the rebuild never started");

        var missing = !File.Exists(IndexOf(repo));
        var setAside = Backups(repo).Length;
        release.Set();
        await repair;

        Assert.False(missing, "the index file was gone while the new one was built");
        Assert.Equal(0, setAside);
        Assert.Single(Backups(repo));
        Assert.True(new FileInfo(IndexOf(repo)).Length > 20);
    }

    [Fact]
    public async Task A_repair_that_cannot_swap_the_index_in_because_another_program_holds_it_says_so_and_changes_nothing()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "another program can hold a file open against being replaced only on Windows");
        using var ws = new EngineWorkspace(options => options with { LockRetryDelays = [TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10)] });
        var (repo, _) = await ws.ThreePointsRepoAsync();
        Empty(repo);
        var engine = ws.Engine.ForGameLockHolder();

        GitOperationException error;
        using (new FileStream(IndexOf(repo), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            error = await Assert.ThrowsAsync<GitOperationException>(() => engine.GetStatusAsync(repo, Ct));
        }

        Assert.Equal("error.gitOperation.checkoutFailed", error.MessageId);
        Assert.Equal(IndexOf(repo), error.Args["path"]);
        Assert.Empty(Backups(repo));
        Assert.Equal(0, new FileInfo(IndexOf(repo)).Length);
        Assert.Empty(Directory.GetFiles(repo.GitDir, "*.tmp"));
        Assert.False(File.Exists(Path.Combine(repo.GitDir, "index.lock")));
    }

    [Fact]
    public async Task The_rebuilt_index_knows_the_files_so_the_next_status_does_not_read_them()
    {
        var (repo, _) = await _ws.ThreePointsRepoAsync();
        _ws.AgeFiles(repo);
        Empty(repo);

        await _locked.RepairIndexIfDamagedAsync(repo, Ct);

        // git's own listing of the stamps it keeps per file: an entry git has not looked at shows size 0 and is read in full by every status.
        var stamps = await _ws.GitOutAsync(repo, "ls-files", "--debug", "a.txt");
        Assert.Contains($"size: {"three".Length}", stamps, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_repair_started_by_a_question_asked_of_an_empty_folder_still_stamps_the_files_of_the_real_one()
    {
        var (repo, _) = await _ws.ThreePointsRepoAsync();
        _ws.AgeFiles(repo);
        _ws.Write(repo, "a.txt", "edited and not saved");

        // The question about which recorded files the patterns name is asked of a work tree that holds nothing. The file is zero-filled
        // with its old time stamp, so the call it spoils is the first to find it damaged.
        var damaged = false;
        _ws.OnInvoke = invocation =>
        {
            if (!damaged && invocation.Arguments.Contains("-i"))
            {
                damaged = true;
                var written = File.GetLastWriteTimeUtc(IndexOf(repo));
                ZeroFill(repo);
                File.SetLastWriteTimeUtc(IndexOf(repo), written);
            }
            else if (damaged && invocation.Arguments[0] == "add")
            {
                // Stops the save once the index is rebuilt, before any later step can stamp the files itself.
                throw new InvalidOperationException("stopped for the test");
            }
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => _locked.CaptureAsync(repo, new CaptureRequest("profile-1", "Edited", Guid.NewGuid().ToString()) { IgnorePatterns = ["*.log"] }, null, Ct));

        Assert.True(damaged, "the question was never asked");
        var stamps = await _ws.GitOutAsync(repo, "ls-files", "--debug", "b.txt");
        Assert.Contains($"size: {"two".Length}", stamps, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_set_aside_index_is_left_by_the_sweep_of_stale_files()
    {
        var (repo, _) = await _ws.ThreePointsRepoAsync();
        Empty(repo);
        await _locked.RepairIndexIfDamagedAsync(repo, Ct);
        var kept = Assert.Single(Backups(repo));

        _ws.Engine.RemoveStaleLocks(repo.GitDir);
        _ws.Write(repo, "c.txt", "new");
        await CaptureAsync(repo, "Another");

        Assert.True(File.Exists(kept));
    }

    [Fact]
    public async Task A_switch_that_git_finished_but_did_not_verify_finishes_after_the_index_was_repaired()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");
        await _ws.CaptureAsync(repo, "profile-a");
        var a = TreeSnapshot.Take(repo.WorkTree);
        await _ws.GitWriteAsync(repo, "checkout", "--quiet", "-b", "profile-b");
        _ws.Write(repo, "a.txt", "b");
        await _ws.CaptureAsync(repo, "profile-b");
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        _ws.OnInvoke = invocation =>
        {
            if (EngineWorkspace.IsVerifyDiff(invocation))
            {
                cancel.Cancel();
            }
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _locked.CheckoutBranchAsync(repo, "profile-a", ct: cancel.Token));
        _ws.OnInvoke = null;
        Assert.NotNull((await _locked.GetStatusAsync(repo, Ct)).InterruptedSwitch);
        Empty(repo);

        await _locked.CheckoutBranchAsync(repo, "profile-a", ct: Ct);

        Assert.Equal(a, TreeSnapshot.Take(repo.WorkTree));
        Assert.Null((await _locked.GetStatusAsync(repo, Ct)).InterruptedSwitch);
    }

    [Fact]
    public async Task Discarding_the_mix_of_an_unfinished_switch_works_when_the_index_is_damaged()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");
        await _ws.CaptureAsync(repo, "profile-a");
        await _ws.GitWriteAsync(repo, "checkout", "--quiet", "-b", "profile-b");
        _ws.Write(repo, "a.txt", "b");
        await _ws.CaptureAsync(repo, "profile-b");
        var b = TreeSnapshot.Take(repo.WorkTree);
        SwitchJournal.Write(repo.GitDir, await _ws.TipAsync(repo, "profile-b"), (await _ws.TipAsync(repo, "profile-a"))!, "profile-a");
        _ws.Write(repo, "a.txt", "half written");
        Empty(repo);

        await _locked.DiscardChangesAsync(repo, Ct);

        Assert.Equal(b, TreeSnapshot.Take(repo.WorkTree));
        Assert.Null((await _locked.GetStatusAsync(repo, Ct)).InterruptedSwitch);
    }

    [Theory]
    [InlineData("checkout")]
    [InlineData("verify")]
    public async Task Once_a_switch_has_written_the_index_itself_damage_is_reported_and_not_repaired(string where)
    {
        var (repo, points) = await _ws.ThreePointsRepoAsync();
        _ws.OnInvoke = invocation =>
        {
            if (where == "checkout" ? invocation.Arguments[0] == "checkout" : EngineWorkspace.IsVerifyDiff(invocation))
            {
                Empty(repo);
            }
        };

        var error = await Record.ExceptionAsync(() => _locked.CheckoutCommitAsync(repo, points[0], ct: Ct));

        Assert.IsType<GitOperationException>(error);
        Assert.Empty(Backups(repo));
    }

    [Fact]
    public async Task The_next_operation_repairs_again_after_a_switch_refused_to()
    {
        var (repo, points) = await _ws.ThreePointsRepoAsync();
        _ws.OnInvoke = invocation =>
        {
            if (invocation.Arguments[0] == "checkout")
            {
                Empty(repo);
            }
        };
        await Record.ExceptionAsync(() => _locked.CheckoutCommitAsync(repo, points[0], ct: Ct));
        _ws.OnInvoke = null;

        var failure = await Record.ExceptionAsync(() => _locked.GetStatusAsync(repo, Ct));

        Assert.Null(failure);
        Assert.Single(Backups(repo));
    }

    public static TheoryData<string, string> RebuildFailures => new()
    {
        { "error: No space left on device\nfatal: unable to write new index file\n", "error.diskSpace.low" },
        { "error: There is not enough space on the disk.\nfatal: unable to write new index file\n", "error.diskSpace.low" },
        { "fatal: Unable to create 'C:/Store/game/install.git/index.lock': File exists.\n", "error.gitOperation.failed" },
        { "fatal: Unable to create 'C:/Store/game/install.git/index.lock': Permission denied\n", "error.storage.writeFailed" },
        { "error: unable to create temporary file: Access is denied.\nfatal: unable to write new index file\n", "error.storage.writeFailed" },
        { "fatal: failed to unpack tree object HEAD\n", "error.gitOperation.recordDamaged" },
        { "fatal: Not a valid object name HEAD\n", "error.gitOperation.recordDamaged" },
    };

    [Theory]
    [MemberData(nameof(RebuildFailures))]
    public void A_failure_to_rebuild_is_reported_as_what_it_was_and_only_damage_is_called_damage(string standardError, string messageId)
    {
        var failure = _locked.RepairFailureFor(new GitCommandException(["read-tree", "HEAD"], 128, standardError), Path.Combine(_ws.StorageRoot, "game-1", "install.git"));

        Assert.Equal(messageId, Assert.IsAssignableFrom<SavepointException>(failure).MessageId);
    }

    [Fact]
    public async Task A_repair_that_could_not_be_done_is_written_to_the_log()
    {
        var (repo, _) = await _ws.ThreePointsRepoAsync();
        await RemoveObjectAsync(repo, (await _ws.ResolveAsync(repo, "profile-1"))!);
        Empty(repo);
        var log = new RecordingDiagnosticLog();
        var engine = new GitEngine(new GitRunner(_ws.Binary, new GitRunnerOptions { Log = log }), _ws.PathSafety, _ws.Space.Probe, _ws.StorageRoot).ForGameLockHolder();

        await Assert.ThrowsAsync<RepositoryDamagedException>(() => engine.ListCaptureCandidatesAsync(repo, [], Ct));

        Assert.Contains(log.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("could not be rebuilt", StringComparison.Ordinal));
    }
}
