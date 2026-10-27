using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// A switch killed while git writes files leaves HEAD and the index at the old point and the folder part way to the new one. Switching
// again to the same point must finish it without running over anything the player changed outside the files that switch writes.
[Collection(EngineCollections.F)]
public sealed class FinishSwitchTests : IDisposable
{
    private readonly List<string> _hashed = [];
    private readonly EngineWorkspace _ws;

    public FinishSwitchTests()
    {
        _ws = new(options => options with
        {
            FileHashed = path =>
            {
                lock (_hashed)
                {
                    _hashed.Add(path);
                }
            },
        });
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    // Profile-a: common.cfg, shared.dat, Data/mod.pak, a-only.txt. Profile-b is made from it and changes shared.dat and Data/mod.pak, drops
    // a-only.txt and adds b-only.txt and logs/b.log. The folder is on profile-b; the third value is what profile-a's folder looks like.
    private async Task<(RepoRef Repo, (string A, string B, SortedDictionary<string, string> ASnapshot) Data)> ProfilesAsync() =>
        await RepoTemplates.CopyAsync("finish-switch", _ws, async ws =>
        {
            var repo = await ws.InitializedFreshAsync();
            ws.Write(repo, "common.cfg", "cfg");
            ws.Write(repo, "shared.dat", "a's data");
            ws.Write(repo, "Data/mod.pak", "a's mod pack, whole");
            ws.Write(repo, "a-only.txt", "a");
            var a = (await ws.CaptureAsync(repo, "profile-a", "A")).TipCommitId!;
            var snapshot = TreeSnapshot.Take(repo.WorkTree);
            await ws.Engine.CreateBranchAsync(repo, "profile-b", "profile-a", TestContext.Current.CancellationToken);
            await ws.Engine.CheckoutBranchAsync(repo, "profile-b", ct: TestContext.Current.CancellationToken);
            ws.Write(repo, "shared.dat", "b's data");
            ws.Write(repo, "Data/mod.pak", "b's mod pack");
            ws.Delete(repo, "a-only.txt");
            ws.Write(repo, "b-only.txt", "b");
            ws.Write(repo, "logs/b.log", "b's log");
            var b = (await ws.CaptureAsync(repo, "profile-b", "B")).TipCommitId!;
            return (repo, (a, b, snapshot));
        });

    // What a switch to profile-a leaves when it is killed part way: shared.dat is rewritten, Data/mod.pak is cut short, a-only.txt is
    // there already, b-only.txt is gone and logs/b.log is not yet. HEAD and the index still say profile-b. Git removes a file it
    // changes before it writes the new copy, so each rewritten file goes the same way.
    private void KillTheSwitchPartWay(RepoRef repo, string a, string b)
    {
        SwitchJournal.Write(repo.GitDir, b, a, "profile-a");
        RemoveThenWrite(repo, "shared.dat", "a's data");
        RemoveThenWrite(repo, "Data/mod.pak", "a's mo");
        _ws.Write(repo, "a-only.txt", "a");
        _ws.Delete(repo, "b-only.txt");
    }

    private void RemoveThenWrite(RepoRef repo, string path, string content)
    {
        _ws.Delete(repo, path);
        _ws.Write(repo, path, content);
    }

    private static readonly DateTime _longAgo = new(2020, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task A_file_the_killed_switch_removed_before_it_wrote_the_new_copy_is_written_by_the_finish()
    {
        var (repo, data) = await ProfilesAsync();
        KillTheSwitchPartWay(repo, data.A, data.B);
        _ws.Delete(repo, "shared.dat");
        _ws.Delete(repo, "Data/mod.pak");

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);

        Assert.Equal(data.ASnapshot, TreeSnapshot.Take(repo.WorkTree));
        Assert.Null(SwitchJournal.Read(repo.GitDir));
        Assert.True((await _ws.Engine.GetStatusAsync(repo, Ct)).IsClean);
    }

    [Fact]
    public async Task A_switch_killed_after_the_index_reached_the_target_but_before_head_moved_is_finished_and_leaves_the_right_files_alone()
    {
        var (repo, data) = await ProfilesAsync();
        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);
        await _ws.GitWriteAsync(repo, "symbolic-ref", "HEAD", "refs/heads/profile-b");
        SwitchJournal.Write(repo.GitDir, data.B, data.A, "profile-a");
        File.SetLastWriteTimeUtc(_ws.FileOf(repo, "shared.dat"), _longAgo);

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);

        Assert.Equal("refs/heads/profile-a", await _ws.HeadRefAsync(repo));
        Assert.Equal(data.ASnapshot, TreeSnapshot.Take(repo.WorkTree));
        Assert.Null(SwitchJournal.Read(repo.GitDir));
        Assert.True((await _ws.Engine.GetStatusAsync(repo, Ct)).IsClean);
        Assert.Equal(_longAgo, File.GetLastWriteTimeUtc(_ws.FileOf(repo, "shared.dat")));
    }

    [Fact]
    public async Task A_file_that_already_holds_the_targets_bytes_is_not_written_again()
    {
        var (repo, data) = await ProfilesAsync();
        KillTheSwitchPartWay(repo, data.A, data.B);
        File.SetLastWriteTimeUtc(_ws.FileOf(repo, "shared.dat"), _longAgo);

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);

        Assert.Equal(data.ASnapshot, TreeSnapshot.Take(repo.WorkTree));
        Assert.Equal(_longAgo, File.GetLastWriteTimeUtc(_ws.FileOf(repo, "shared.dat")));
        Assert.True((await _ws.Engine.GetStatusAsync(repo, Ct)).IsClean);
    }

    [Fact]
    public async Task A_file_of_the_targets_size_with_other_bytes_is_written_again()
    {
        var (repo, data) = await ProfilesAsync();
        KillTheSwitchPartWay(repo, data.A, data.B);
        RemoveThenWrite(repo, "shared.dat", "x's data");

        // Of the same size as the target's copy, it still is not the target's copy.
        Assert.Contains("shared.dat", (await _ws.Engine.ReviewStoppedSwitchAsync(repo, Ct)).ReplacedPaths);

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);

        Assert.Equal(data.ASnapshot, TreeSnapshot.Take(repo.WorkTree));
        Assert.True((await _ws.Engine.GetStatusAsync(repo, Ct)).IsClean);
    }

    [Fact]
    public async Task A_change_the_player_made_to_a_file_the_switch_was_writing_is_replaced_by_the_finish()
    {
        var (repo, data) = await ProfilesAsync();
        KillTheSwitchPartWay(repo, data.A, data.B);
        _ws.Write(repo, "shared.dat", "the player's own edit, made after the switch stopped");

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);

        Assert.Equal("a's data", File.ReadAllText(_ws.FileOf(repo, "shared.dat")));
        Assert.Equal(data.ASnapshot, TreeSnapshot.Take(repo.WorkTree));
    }

    [Fact]
    public async Task An_edited_file_the_switch_would_remove_refuses_before_anything_is_written()
    {
        var (repo, data) = await ProfilesAsync();
        KillTheSwitchPartWay(repo, data.A, data.B);
        _ws.Write(repo, "logs/b.log", "the player's edit to a file the switch removes");
        var before = TreeSnapshot.Take(repo.WorkTree);

        await Assert.ThrowsAsync<DirtyWorkingTreeException>(() => _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct));

        Assert.Equal(before, TreeSnapshot.Take(repo.WorkTree));
        Assert.Equal(new InterruptedSwitch(data.B, data.A, "profile-a"), SwitchJournal.Read(repo.GitDir));
        Assert.Equal("refs/heads/profile-b", await _ws.HeadRefAsync(repo));
    }

    [Fact]
    public async Task The_switch_check_refuses_an_edited_file_the_switch_would_remove()
    {
        var (repo, data) = await ProfilesAsync();
        KillTheSwitchPartWay(repo, data.A, data.B);
        _ws.Write(repo, "logs/b.log", "the player's edit to a file the switch removes");

        await Assert.ThrowsAsync<DirtyWorkingTreeException>(() => _ws.Engine.CheckSwitchAsync(repo, "profile-a", isBranch: true, Ct));
    }

    private const int _manyFiles = 450;

    // Profile-a holds 450 small files and profile-b other content in the same files; the folder ends on profile-b. The third value is profile-a's folder.
    private async Task<(RepoRef Repo, (string A, string B, SortedDictionary<string, string> ASnapshot) Data)> ManyFilesAsync() =>
        await RepoTemplates.CopyAsync("finish-switch-many", _ws, async ws =>
        {
            var repo = await ws.InitializedFreshAsync();
            ws.WriteMany(repo, "Data", _manyFiles, content: "a");
            var a = (await ws.CaptureAsync(repo, "profile-a", "A")).TipCommitId!;
            var snapshot = TreeSnapshot.Take(repo.WorkTree);
            await ws.Engine.CreateBranchAsync(repo, "profile-b", "profile-a", TestContext.Current.CancellationToken);
            await ws.Engine.CheckoutBranchAsync(repo, "profile-b", ct: TestContext.Current.CancellationToken);
            ws.WriteMany(repo, "Data", _manyFiles, content: "b");
            var b = (await ws.CaptureAsync(repo, "profile-b", "B")).TipCommitId!;
            return (repo, (a, b, snapshot));
        });

    [Fact]
    public async Task Finishing_reports_the_files_it_writes_though_git_prints_no_meter_for_them()
    {
        var (repo, data) = await ManyFilesAsync();

        // Every file was removed and its new copy cut short when the switch was killed.
        SwitchJournal.Write(repo.GitDir, data.B, data.A, "profile-a");
        _ws.WriteMany(repo, "Data", _manyFiles, content: "cut");
        var log = new ProgressLog();

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", log, Ct);

        Assert.Contains(log.Reports, report => report.FilesDone >= _manyFiles);
        Assert.All(log.Reports, report => Assert.True(report.FilesTotal is null || report.FilesTotal >= report.FilesDone));
        Assert.Equal("a 0", File.ReadAllText(_ws.FileOf(repo, "Data/f0.txt")));

        // The bar never goes back: git's own count for what is left starts below the files already written.
        Assert.Equal(log.Reports.Select(report => report.FilesDone).Order(), log.Reports.Select(report => report.FilesDone));
        Assert.Equal(log.Reports.Select(report => report.BytesDone ?? 0).Order(), log.Reports.Select(report => report.BytesDone ?? 0));
    }

    [Fact]
    public async Task A_batch_of_the_finish_that_fails_part_way_leaves_the_note_and_the_repeat_finishes()
    {
        var (repo, data) = await ManyFilesAsync();
        SwitchJournal.Write(repo.GitDir, data.B, data.A, "profile-a");
        _ws.WriteMany(repo, "Data", _manyFiles, content: "cut");
        var batches = 0;
        _ws.OnInvoke = invocation =>
        {
            if (invocation.Arguments[0] == "checkout" && invocation.Arguments.Contains("--pathspec-from-file=-") && ++batches == 2)
            {
                throw new GitOperationException("the second batch could not be written");
            }
        };

        await Assert.ThrowsAsync<GitOperationException>(() => _ws.Engine.CheckoutBranchAsync(repo, "profile-a", new ProgressLog(), Ct));

        // The first batch is written and the rest is not: the folder is as a kill leaves it, and the note says which switch it is.
        _ws.OnInvoke = null;
        Assert.Equal(new InterruptedSwitch(data.B, data.A, "profile-a"), SwitchJournal.Read(repo.GitDir));
        Assert.Equal("refs/heads/profile-b", await _ws.HeadRefAsync(repo));
        var contents = Directory.EnumerateFiles(_ws.FileOf(repo, "Data")).Select(File.ReadAllText).ToArray();
        Assert.Equal(200, contents.Count(content => content.StartsWith("a ", StringComparison.Ordinal)));
        Assert.Equal(_manyFiles - 200, contents.Count(content => content.StartsWith("cut", StringComparison.Ordinal)));

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);

        Assert.Equal(data.ASnapshot, TreeSnapshot.Take(repo.WorkTree));
        Assert.Null(SwitchJournal.Read(repo.GitDir));
        Assert.True((await _ws.Engine.GetStatusAsync(repo, Ct)).IsClean);
    }

    [Fact]
    public async Task A_file_held_open_during_the_finish_is_named_and_a_repeat_after_release_finishes()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows refuses to replace a file another program holds open");
        var (repo, data) = await ProfilesAsync();
        KillTheSwitchPartWay(repo, data.A, data.B);
        var locked = _ws.FileOf(repo, "Data/mod.pak");

        var held = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        try
        {
            var error = await Assert.ThrowsAsync<GitOperationException>(() => _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct));

            Assert.Equal(locked, error.Args["path"]);
        }
        finally
        {
            await held.DisposeAsync();
        }

        Assert.Equal(new InterruptedSwitch(data.B, data.A, "profile-a"), SwitchJournal.Read(repo.GitDir));
        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);
        Assert.Equal(data.ASnapshot, TreeSnapshot.Take(repo.WorkTree));
        Assert.Null(SwitchJournal.Read(repo.GitDir));
    }

    [Fact]
    public async Task The_switch_check_reads_no_file_to_compare_it()
    {
        var (repo, data) = await ProfilesAsync();
        KillTheSwitchPartWay(repo, data.A, data.B);

        await _ws.Engine.CheckSwitchAsync(repo, "profile-a", isBranch: true, Ct);

        Assert.Empty(_hashed);
    }

    [Fact]
    public async Task A_review_that_does_not_list_the_replaced_files_reads_no_file_and_still_names_what_blocks()
    {
        var (repo, data) = await ProfilesAsync();
        KillTheSwitchPartWay(repo, data.A, data.B);
        _ws.Write(repo, "common.cfg", "the player's edit");

        var review = await _ws.Engine.ReviewStoppedSwitchAsync(repo, Ct, listReplaced: false);

        Assert.Equal(["common.cfg"], review.BlockingPaths);
        Assert.Empty(review.ReplacedPaths);
        Assert.Empty(_hashed);
    }

    [Fact]
    public async Task The_switch_reads_each_file_of_the_targets_size_once()
    {
        var (repo, data) = await ProfilesAsync();
        KillTheSwitchPartWay(repo, data.A, data.B);

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);

        Assert.NotEmpty(_hashed);
        Assert.Equal(_hashed.Distinct().Count(), _hashed.Count);
    }

    [Fact]
    public async Task The_review_names_the_files_the_finish_would_replace_and_nothing_that_blocks_it()
    {
        var (repo, data) = await ProfilesAsync();
        KillTheSwitchPartWay(repo, data.A, data.B);
        var before = TreeSnapshot.Take(repo.WorkTree);

        var review = await _ws.Engine.ReviewStoppedSwitchAsync(repo, Ct);

        Assert.Equal(["Data/mod.pak"], review.ReplacedPaths);
        Assert.Empty(review.BlockingPaths);
        Assert.Equal(before, TreeSnapshot.Take(repo.WorkTree));
    }

    [Fact]
    public async Task The_review_lists_a_change_the_player_made_to_a_file_the_switch_was_writing_as_replaced()
    {
        var (repo, data) = await ProfilesAsync();
        KillTheSwitchPartWay(repo, data.A, data.B);
        _ws.Write(repo, "shared.dat", "the player's own edit, made after the switch stopped");

        var review = await _ws.Engine.ReviewStoppedSwitchAsync(repo, Ct);

        Assert.Equal(["Data/mod.pak", "shared.dat"], review.ReplacedPaths.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task The_review_names_the_changes_that_block_the_finish()
    {
        var (repo, data) = await ProfilesAsync();
        KillTheSwitchPartWay(repo, data.A, data.B);
        _ws.Write(repo, "common.cfg", "the player's edit");
        _ws.Write(repo, "notes.txt", "the player's note");
        _ws.Write(repo, "logs/b.log", "the player's edit to a file the switch removes");

        var review = await _ws.Engine.ReviewStoppedSwitchAsync(repo, Ct);

        Assert.Equal(["common.cfg", "logs/b.log", "notes.txt"], review.BlockingPaths.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task The_review_of_a_folder_with_no_stopped_switch_is_empty()
    {
        var (repo, _) = await ProfilesAsync();
        _ws.Write(repo, "common.cfg", "unsaved, but no switch stopped");

        var review = await _ws.Engine.ReviewStoppedSwitchAsync(repo, Ct);

        Assert.Empty(review.BlockingPaths);
        Assert.Empty(review.ReplacedPaths);
    }

    [Theory]
    [InlineData("branch")]
    [InlineData("commit")]
    public async Task Switching_again_finishes_a_switch_that_was_killed_part_way_and_clears_the_note(string kind)
    {
        var (repo, data) = await ProfilesAsync();
        KillTheSwitchPartWay(repo, data.A, data.B);

        if (kind == "branch")
        {
            await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);
            Assert.Equal("refs/heads/profile-a", await _ws.HeadRefAsync(repo));
        }
        else
        {
            await _ws.Engine.CheckoutCommitAsync(repo, data.A, ct: Ct);
            Assert.Null(await _ws.HeadRefAsync(repo));
            Assert.Equal(data.A, await _ws.ResolveAsync(repo, "HEAD"));
        }

        Assert.Null(SwitchJournal.Read(repo.GitDir));
        Assert.Equal(data.ASnapshot, TreeSnapshot.Take(repo.WorkTree));
        var status = await _ws.Engine.GetStatusAsync(repo, Ct);
        Assert.True(status.IsClean);
        Assert.Null(status.InterruptedSwitch);
    }

    [Fact]
    public async Task The_switch_check_passes_for_the_switch_that_was_killed_part_way()
    {
        var (repo, data) = await ProfilesAsync();
        KillTheSwitchPartWay(repo, data.A, data.B);

        await _ws.Engine.CheckSwitchAsync(repo, "profile-a", isBranch: true, Ct);

        Assert.NotNull(SwitchJournal.Read(repo.GitDir));
    }

    [Theory]
    [InlineData("edit")]
    [InlineData("new")]
    [InlineData("delete")]
    public async Task An_unsaved_change_outside_the_files_the_killed_switch_writes_still_refuses_and_nothing_is_written(string change)
    {
        var (repo, data) = await ProfilesAsync();
        KillTheSwitchPartWay(repo, data.A, data.B);
        switch (change)
        {
            case "edit":
                _ws.Write(repo, "common.cfg", "the player's edit");
                break;
            case "new":
                _ws.Write(repo, "notes.txt", "the player's note");
                break;
            default:
                _ws.Delete(repo, "common.cfg");
                break;
        }

        var before = TreeSnapshot.Take(repo.WorkTree);

        await Assert.ThrowsAsync<DirtyWorkingTreeException>(() => _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct));

        Assert.Equal(before, TreeSnapshot.Take(repo.WorkTree));
        Assert.Equal(new InterruptedSwitch(data.B, data.A, "profile-a"), SwitchJournal.Read(repo.GitDir));
        Assert.Equal("refs/heads/profile-b", await _ws.HeadRefAsync(repo));
    }

    [Theory]
    [InlineData("edit")]
    [InlineData("new")]
    public async Task The_switch_check_refuses_an_unsaved_change_outside_the_files_the_killed_switch_writes(string change)
    {
        var (repo, data) = await ProfilesAsync();
        KillTheSwitchPartWay(repo, data.A, data.B);
        _ws.Write(repo, change == "edit" ? "common.cfg" : "notes.txt", "the player's own work");

        await Assert.ThrowsAsync<DirtyWorkingTreeException>(() => _ws.Engine.CheckSwitchAsync(repo, "profile-a", isBranch: true, Ct));
    }

    [Fact]
    public async Task The_same_switch_finishes_once_the_player_puts_an_outside_change_back()
    {
        var (repo, data) = await ProfilesAsync();
        KillTheSwitchPartWay(repo, data.A, data.B);
        _ws.Write(repo, "common.cfg", "the player's edit");
        await Assert.ThrowsAsync<DirtyWorkingTreeException>(() => _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct));

        // Putting the file back lets the same switch finish.
        _ws.Write(repo, "common.cfg", "cfg");
        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);

        Assert.Equal(data.ASnapshot, TreeSnapshot.Take(repo.WorkTree));
        Assert.Null(SwitchJournal.Read(repo.GitDir));
    }

    [Fact]
    public async Task A_forced_switch_still_finishes_a_switch_that_was_killed_part_way()
    {
        var (repo, data) = await ProfilesAsync();
        KillTheSwitchPartWay(repo, data.A, data.B);

        await _ws.Engine.ForceCheckoutBranchAsync(repo, "profile-a", Ct);

        Assert.Equal("refs/heads/profile-a", await _ws.HeadRefAsync(repo));
        Assert.Null(SwitchJournal.Read(repo.GitDir));
        Assert.Equal(data.ASnapshot, TreeSnapshot.Take(repo.WorkTree));
        Assert.True((await _ws.Engine.GetStatusAsync(repo, Ct)).IsClean);
    }

    [Fact]
    public async Task Discarding_still_returns_a_switch_that_was_killed_part_way_to_the_point_it_started_from()
    {
        var (repo, data) = await ProfilesAsync();
        var b = TreeSnapshot.Take(repo.WorkTree);
        KillTheSwitchPartWay(repo, data.A, data.B);

        await _ws.Engine.DiscardChangesAsync(repo, Ct);

        Assert.Equal(b, TreeSnapshot.Take(repo.WorkTree));
        Assert.Null(SwitchJournal.Read(repo.GitDir));
        Assert.Equal("refs/heads/profile-b", await _ws.HeadRefAsync(repo));
        Assert.True((await _ws.Engine.GetStatusAsync(repo, Ct)).IsClean);
    }

    [Fact]
    public async Task Finishing_leaves_the_files_the_games_patterns_name_as_the_player_has_them()
    {
        var (repo, data) = await ProfilesAsync();
        repo = repo with { IgnorePatterns = ["logs/"] };
        _ws.Write(repo, "logs/b.log", "the player's own log");
        KillTheSwitchPartWay(repo, data.A, data.B);

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);

        Assert.Equal("the player's own log", File.ReadAllText(_ws.FileOf(repo, "logs/b.log")));
        var folder = TreeSnapshot.Take(repo.WorkTree);
        Assert.True(folder.Remove("logs/b.log"));
        Assert.Equal(data.ASnapshot, folder);
        Assert.Null(SwitchJournal.Read(repo.GitDir));
        Assert.True((await _ws.Engine.GetStatusAsync(repo, Ct)).IsClean);
    }

    [Fact]
    public async Task A_new_file_the_games_patterns_name_does_not_stop_the_finish()
    {
        var (repo, data) = await ProfilesAsync();
        repo = repo with { IgnorePatterns = ["logs/"] };
        _ws.Write(repo, "logs/b.log", "the player's own log");
        _ws.Write(repo, "logs/new.log", "the game's new log");
        KillTheSwitchPartWay(repo, data.A, data.B);

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);

        Assert.Equal("the game's new log", File.ReadAllText(_ws.FileOf(repo, "logs/new.log")));
        Assert.Null(SwitchJournal.Read(repo.GitDir));
    }

    [Fact]
    public async Task A_killed_switch_that_left_half_a_file_where_a_folder_was_is_finished_too()
    {
        var (repo, data) = await FileAndFolderProfilesAsync();
        SwitchJournal.Write(repo.GitDir, data.B, data.A, "profile-a");

        // The folder Mods/Foo is gone and the file that takes its place is half written.
        _ws.Delete(repo, "Mods/Foo/x.txt");
        Directory.Delete(_ws.FileOf(repo, "Mods/Foo"));
        _ws.Write(repo, "Mods/Foo", "a's file, cut sh");

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);

        Assert.Equal("a's file", File.ReadAllText(_ws.FileOf(repo, "Mods/Foo")));
        Assert.Null(SwitchJournal.Read(repo.GitDir));
        Assert.True((await _ws.Engine.GetStatusAsync(repo, Ct)).IsClean);
    }

    [Fact]
    public async Task A_killed_switch_that_finished_the_file_where_a_folder_was_is_finished_too()
    {
        var (repo, data) = await FileAndFolderProfilesAsync();
        SwitchJournal.Write(repo.GitDir, data.B, data.A, "profile-a");

        // The folder is gone and the file that takes its place is whole, but the index and HEAD still say profile-b.
        _ws.Delete(repo, "Mods/Foo/x.txt");
        Directory.Delete(_ws.FileOf(repo, "Mods/Foo"));
        _ws.Write(repo, "Mods/Foo", "a's file");

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);

        Assert.Equal("a's file", File.ReadAllText(_ws.FileOf(repo, "Mods/Foo")));
        Assert.Null(SwitchJournal.Read(repo.GitDir));
        Assert.True((await _ws.Engine.GetStatusAsync(repo, Ct)).IsClean);
    }

    [Fact]
    public async Task A_killed_switch_that_left_a_folder_where_a_file_was_is_finished_too()
    {
        var (repo, data) = await FileAndFolderProfilesAsync();
        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);
        SwitchJournal.Write(repo.GitDir, data.A, data.B, "profile-b");

        // The file Mods/Foo is gone and the folder that takes its place holds half of x.txt, but HEAD and the index still say profile-a.
        _ws.Delete(repo, "Mods/Foo");
        _ws.Write(repo, "Mods/Foo/x.txt", "b's");

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-b", ct: Ct);

        Assert.Equal("b's x", File.ReadAllText(_ws.FileOf(repo, "Mods/Foo/x.txt")));
        Assert.Null(SwitchJournal.Read(repo.GitDir));
        Assert.True((await _ws.Engine.GetStatusAsync(repo, Ct)).IsClean);
    }

    [Fact]
    public async Task The_review_lists_a_file_the_player_put_where_a_folder_was_as_replaced()
    {
        var (repo, data) = await FileAndFolderProfilesAsync();
        SwitchJournal.Write(repo.GitDir, data.B, data.A, "profile-a");
        _ws.Delete(repo, "Mods/Foo/x.txt");
        Directory.Delete(_ws.FileOf(repo, "Mods/Foo"));
        _ws.Write(repo, "Mods/Foo", "the player's own file, made after the switch stopped");

        var review = await _ws.Engine.ReviewStoppedSwitchAsync(repo, Ct);

        Assert.Equal(["Mods/Foo"], review.ReplacedPaths);
        Assert.Empty(review.BlockingPaths);
    }

    [Fact]
    public async Task The_review_does_not_list_the_whole_target_copy_standing_where_a_folder_was_as_replaced()
    {
        var (repo, data) = await FileAndFolderProfilesAsync();
        SwitchJournal.Write(repo.GitDir, data.B, data.A, "profile-a");
        _ws.Delete(repo, "Mods/Foo/x.txt");
        Directory.Delete(_ws.FileOf(repo, "Mods/Foo"));
        _ws.Write(repo, "Mods/Foo", "a's file");

        var review = await _ws.Engine.ReviewStoppedSwitchAsync(repo, Ct);

        Assert.Empty(review.ReplacedPaths);
        Assert.Empty(review.BlockingPaths);
    }

    [Fact]
    public async Task A_file_held_open_where_a_folder_was_is_named_and_a_repeat_after_release_finishes()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows refuses to remove a file another program holds open");
        var (repo, data) = await FileAndFolderProfilesAsync();
        SwitchJournal.Write(repo.GitDir, data.B, data.A, "profile-a");
        _ws.Delete(repo, "Mods/Foo/x.txt");
        Directory.Delete(_ws.FileOf(repo, "Mods/Foo"));
        _ws.Write(repo, "Mods/Foo", "a's file, cut sh");
        var locked = _ws.FileOf(repo, "Mods/Foo");

        var held = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        try
        {
            var error = await Assert.ThrowsAsync<GitOperationException>(() => _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct));

            Assert.Equal(locked, error.Args["path"]);
        }
        finally
        {
            await held.DisposeAsync();
        }

        Assert.Equal(new InterruptedSwitch(data.B, data.A, "profile-a"), SwitchJournal.Read(repo.GitDir));
        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);
        Assert.Equal("a's file", File.ReadAllText(locked));
        Assert.Null(SwitchJournal.Read(repo.GitDir));
    }

    // Profile-a holds Mods/Foo as a file, profile-b as a folder holding x.txt. The folder is on profile-b.
    private async Task<(RepoRef Repo, (string A, string B) Data)> FileAndFolderProfilesAsync() =>
        await RepoTemplates.CopyAsync("finish-switch-file-folder", _ws, async ws =>
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
