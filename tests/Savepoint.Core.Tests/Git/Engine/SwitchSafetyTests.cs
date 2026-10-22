using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// A switch never runs over work that is not saved: not a shared file the player edited, not a file the game's patterns
// name, not a folder a stopped switch left half done.
[Collection(EngineCollections.C)]
public sealed class SwitchSafetyTests : IDisposable
{
    private readonly EngineWorkspace _ws = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    // profile-a and profile-b share Saves/slot1.sav; a-only.txt is only in a, b-only.txt only in b. The folder is on profile-b.
    private Task<(RepoRef Repo, (string A, string B) Tips)> TwoProfilesAsync() =>
        RepoTemplates.CopyAsync("switch-safety", _ws, async ws =>
        {
            var repo = await ws.InitializedFreshAsync();
            ws.Write(repo, "Saves/slot1.sav", "same in both");
            ws.Write(repo, "a-only.txt", "a");
            var a = (await ws.CaptureAsync(repo, "profile-a", "A")).TipCommitId!;
            await ws.Engine.CreateBranchAsync(repo, "profile-b", "profile-a", TestContext.Current.CancellationToken);
            await ws.Engine.CheckoutBranchAsync(repo, "profile-b", ct: TestContext.Current.CancellationToken);
            ws.Delete(repo, "a-only.txt");
            ws.Write(repo, "b-only.txt", "b");
            var b = (await ws.CaptureAsync(repo, "profile-b", "B")).TipCommitId!;
            return (repo, (a, b));
        });

    // profile-a keeps logs/run.log; profile-b, made from it, does not. The folder is on profile-b.
    private Task<(RepoRef Repo, (string A, string B) Tips)> LogProfilesAsync() =>
        RepoTemplates.CopyAsync("switch-safety-logs", _ws, async ws =>
        {
            var repo = await ws.InitializedFreshAsync();
            ws.Write(repo, "logs/run.log", "a's copy");
            ws.Write(repo, "game.cfg", "cfg");
            var a = (await ws.CaptureAsync(repo, "profile-a", "A")).TipCommitId!;
            await ws.Engine.CreateBranchAsync(repo, "profile-b", "profile-a", TestContext.Current.CancellationToken);
            await ws.Engine.CheckoutBranchAsync(repo, "profile-b", ct: TestContext.Current.CancellationToken);
            ws.Delete(repo, "logs/run.log");
            var b = (await ws.CaptureAsync(repo, "profile-b", "B")).TipCommitId!;
            return (repo, (a, b));
        });

    [Fact]
    public async Task A_switch_refuses_unsaved_edits_to_a_file_both_profiles_share_and_keeps_them()
    {
        var (repo, _) = await TwoProfilesAsync();
        _ws.Write(repo, "Saves/slot1.sav", "the player's unsaved progress");

        var error = await Assert.ThrowsAsync<DirtyWorkingTreeException>(() => _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct));

        Assert.Equal(RepoRole.Install, error.Args["role"]);
        Assert.Equal("the player's unsaved progress", await File.ReadAllTextAsync(_ws.FileOf(repo, "Saves/slot1.sav"), Ct));
        Assert.Equal("refs/heads/profile-b", await _ws.HeadRefAsync(repo));
        Assert.Null((await _ws.Engine.GetStatusAsync(repo, Ct)).InterruptedSwitch);
        Assert.False(File.Exists(SwitchJournal.PathIn(repo.GitDir)));
    }

    [Theory]
    [InlineData("delete")]
    [InlineData("new")]
    public async Task A_deleted_file_and_a_new_file_are_unsaved_work_too(string kind)
    {
        var (repo, _) = await TwoProfilesAsync();
        if (kind == "delete")
        {
            _ws.Delete(repo, "Saves/slot1.sav");
        }
        else
        {
            _ws.Write(repo, "Saves/slot2.sav", "new progress");
        }

        var before = TreeSnapshot.Take(repo.WorkTree);

        await Assert.ThrowsAsync<DirtyWorkingTreeException>(() => _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct));

        Assert.Equal(before, TreeSnapshot.Take(repo.WorkTree));
    }

    [Fact]
    public async Task The_same_goes_for_switching_to_an_older_point_by_id()
    {
        var (repo, tips) = await TwoProfilesAsync();
        _ws.Write(repo, "Saves/slot1.sav", "unsaved");

        await Assert.ThrowsAsync<DirtyWorkingTreeException>(() => _ws.Engine.CheckoutCommitAsync(repo, tips.A, ct: Ct));

        Assert.Equal("unsaved", await File.ReadAllTextAsync(_ws.FileOf(repo, "Saves/slot1.sav"), Ct));
    }

    [Fact]
    public async Task A_file_the_games_patterns_name_is_not_unsaved_work_and_stays_through_a_switch()
    {
        var (repo, _) = await TwoProfilesAsync();
        await _ws.CaptureAsync(repo, "profile-b", ignore: ["*.cache"]);
        _ws.Write(repo, "shaders.cache", "rebuilt by the game");

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);

        Assert.Equal("refs/heads/profile-a", await _ws.HeadRefAsync(repo));
        Assert.Equal("rebuilt by the game", await File.ReadAllTextAsync(_ws.FileOf(repo, "shaders.cache"), Ct));
    }

    [Fact]
    public async Task A_forced_switch_discards_unsaved_work_on_purpose()
    {
        var (repo, _) = await TwoProfilesAsync();
        _ws.Write(repo, "Saves/slot1.sav", "unsaved");
        _ws.Write(repo, "stray.txt", "new");

        await _ws.Engine.ForceCheckoutBranchAsync(repo, "profile-a", Ct);

        Assert.Equal("same in both", await File.ReadAllTextAsync(_ws.FileOf(repo, "Saves/slot1.sav"), Ct));
        Assert.Equal("refs/heads/profile-a", await _ws.HeadRefAsync(repo));
    }

    [Fact]
    public async Task An_edit_made_after_the_check_to_a_file_the_switch_does_not_change_is_left_alone()
    {
        var (repo, _) = await TwoProfilesAsync();
        _ws.OnInvoke = invocation =>
        {
            if (invocation.Arguments[0] == "checkout")
            {
                _ws.Write(repo, "Saves/slot1.sav", "written by a backup tool just now");
            }
        };

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);

        _ws.OnInvoke = null;
        Assert.Equal("written by a backup tool just now", await File.ReadAllTextAsync(_ws.FileOf(repo, "Saves/slot1.sav"), Ct));
        var status = await _ws.Engine.GetStatusAsync(repo, Ct);
        Assert.Contains("Saves/slot1.sav", status.ChangedPaths);
    }

    [Fact]
    public async Task A_refusal_by_git_leaves_no_note_when_this_call_wrote_it()
    {
        var (repo, _) = await TwoProfilesAsync();
        _ws.OnInvoke = invocation =>
        {
            if (invocation.Arguments[0] == "checkout")
            {
                _ws.Write(repo, "b-only.txt", "edited at the last moment");
            }
        };

        await Assert.ThrowsAsync<DirtyWorkingTreeException>(() => _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct));

        _ws.OnInvoke = null;
        Assert.False(File.Exists(SwitchJournal.PathIn(repo.GitDir)));
    }

    [Fact]
    public async Task A_refusal_by_git_keeps_the_note_an_earlier_unfinished_switch_wrote()
    {
        var (repo, tips) = await TwoProfilesAsync();
        SwitchJournal.Write(repo.GitDir, tips.B, tips.A);
        _ws.OnInvoke = invocation =>
        {
            if (invocation.Arguments[0] == "checkout")
            {
                _ws.Write(repo, "b-only.txt", "edited at the last moment");
            }
        };

        await Assert.ThrowsAsync<DirtyWorkingTreeException>(() => _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct));

        _ws.OnInvoke = null;
        Assert.Equal(new InterruptedSwitch(tips.B, tips.A, "profile-a"), SwitchJournal.Read(repo.GitDir));
    }

    [Fact]
    public async Task A_half_switched_folder_refuses_a_switch_to_a_different_point_and_keeps_the_note()
    {
        var (repo, tips) = await TwoProfilesAsync();
        SwitchJournal.Write(repo.GitDir, tips.B, tips.A);

        var error = await Assert.ThrowsAsync<InterruptedSwitchException>(() => _ws.Engine.CheckoutBranchAsync(repo, "profile-b", ct: Ct));

        Assert.Equal("error.launch.switchInterrupted", error.MessageId);
        Assert.Equal(RepoRole.Install, error.Args["role"]);
        Assert.Equal(tips.A, error.Args["targetPoint"]);
        Assert.Equal(new InterruptedSwitch(tips.B, tips.A), SwitchJournal.Read(repo.GitDir));
        Assert.Equal("refs/heads/profile-b", await _ws.HeadRefAsync(repo));
    }

    [Fact]
    public async Task A_half_switched_folder_finishes_when_the_same_switch_is_repeated()
    {
        var (repo, tips) = await TwoProfilesAsync();
        SwitchJournal.Write(repo.GitDir, tips.B, tips.A);

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);

        Assert.Equal("refs/heads/profile-a", await _ws.HeadRefAsync(repo));
        Assert.Null(SwitchJournal.Read(repo.GitDir));
        Assert.True(File.Exists(_ws.FileOf(repo, "a-only.txt")));
        Assert.False(File.Exists(_ws.FileOf(repo, "b-only.txt")));
    }

    [Fact]
    public async Task A_switch_stopped_after_head_moved_with_a_file_left_behind_is_finished_by_repeating_it()
    {
        var (repo, tips) = await TwoProfilesAsync();
        SwitchJournal.Write(repo.GitDir, tips.B, tips.A);
        await _ws.GitWriteAsync(repo, "checkout", "--quiet", "profile-a");
        var a = TreeSnapshot.Take(repo.WorkTree);
        _ws.Write(repo, "b-only.txt", "b");

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);

        Assert.Equal(a, TreeSnapshot.Take(repo.WorkTree));
        Assert.Null(SwitchJournal.Read(repo.GitDir));
        Assert.True((await _ws.Engine.GetStatusAsync(repo, Ct)).IsClean);
    }

    [Fact]
    public async Task Discarding_a_half_switched_folder_returns_it_to_the_point_head_is_on_and_clears_the_note()
    {
        var (repo, tips) = await TwoProfilesAsync();
        SwitchJournal.Write(repo.GitDir, tips.B, tips.A);
        await _ws.GitWriteAsync(repo, "checkout", "--quiet", "profile-a");
        var a = TreeSnapshot.Take(repo.WorkTree);
        _ws.Write(repo, "b-only.txt", "left behind by the stopped switch");
        _ws.Write(repo, "Saves/slot1.sav", "half written");

        await _ws.Engine.DiscardChangesAsync(repo, Ct);

        Assert.Equal(a, TreeSnapshot.Take(repo.WorkTree));
        Assert.Null(SwitchJournal.Read(repo.GitDir));
        Assert.Equal("refs/heads/profile-a", await _ws.HeadRefAsync(repo));
    }

    [Theory]
    [InlineData(CaptureMode.Normal)]
    [InlineData(CaptureMode.Amend)]
    [InlineData(CaptureMode.Fork)]
    [InlineData(CaptureMode.MoveForward)]
    public async Task Every_capture_mode_refuses_a_half_switched_folder_before_it_stages_anything(CaptureMode mode)
    {
        var (repo, tips) = await TwoProfilesAsync();
        SwitchJournal.Write(repo.GitDir, tips.B, tips.A);
        _ws.Write(repo, "Saves/slot2.sav", "something to capture");
        var request = new CaptureRequest(mode == CaptureMode.Fork ? "profile-c" : "profile-b", "Next", Guid.NewGuid().ToString())
        {
            Mode = mode,
            ExpectedStartCommit = tips.B,
        };

        var error = await Assert.ThrowsAsync<InterruptedSwitchException>(() => _ws.Engine.CaptureAsync(repo, request, null, Ct));

        Assert.Equal("error.launch.switchInterrupted", error.MessageId);
        Assert.Equal(RepoRole.Install, error.Args["role"]);
        Assert.Equal(tips.A, error.Args["targetPoint"]);
        Assert.DoesNotContain(_ws.Invocations, invocation => invocation.Arguments.Contains("add") || invocation.Arguments.Contains("commit") || invocation.Arguments.Contains("commit-tree"));
        Assert.Equal(tips.B, await _ws.TipAsync(repo, "profile-b"));
        Assert.Null(await _ws.TipAsync(repo, "profile-c"));
    }

    [Fact]
    public async Task A_capture_works_again_once_the_half_switched_folder_is_discarded()
    {
        var (repo, tips) = await TwoProfilesAsync();
        SwitchJournal.Write(repo.GitDir, tips.B, tips.A);
        await _ws.Engine.DiscardChangesAsync(repo, Ct);
        _ws.Write(repo, "Saves/slot2.sav", "new progress");

        var outcome = await _ws.CaptureAsync(repo, "profile-b");

        Assert.True(outcome.Changed);
    }

    [Fact]
    public async Task A_switch_leaves_a_recorded_file_the_games_patterns_now_name_where_it_is_and_does_not_refuse()
    {
        var (repo, _) = await LogProfilesAsync();
        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);
        await _ws.CaptureAsync(repo, "profile-a", ignore: ["logs/"]);

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-b", ct: Ct);

        Assert.Equal("a's copy", await File.ReadAllTextAsync(_ws.FileOf(repo, "logs/run.log"), Ct));
        Assert.Equal("refs/heads/profile-b", await _ws.HeadRefAsync(repo));
        var status = await _ws.Engine.GetStatusAsync(repo, Ct);
        Assert.True(status.IsClean);
        Assert.Null(status.InterruptedSwitch);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_switch_never_overwrites_the_players_file_the_games_patterns_name_with_an_older_copy(bool force)
    {
        var (repo, _) = await LogProfilesAsync();
        await _ws.CaptureAsync(repo, "profile-b", ignore: ["logs/"]);
        _ws.Write(repo, "logs/run.log", "the player's own log");

        if (force)
        {
            await _ws.Engine.ForceCheckoutBranchAsync(repo, "profile-a", Ct);
        }
        else
        {
            await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);
        }

        Assert.Equal("the player's own log", await File.ReadAllTextAsync(_ws.FileOf(repo, "logs/run.log"), Ct));
        Assert.Equal("refs/heads/profile-a", await _ws.HeadRefAsync(repo));
        Assert.True((await _ws.Engine.GetStatusAsync(repo, Ct)).IsClean);
    }

    [Fact]
    public async Task Files_the_games_patterns_do_not_name_are_switched_as_usual_beside_the_ones_they_name()
    {
        var (repo, _) = await NamedProfilesAsync();

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);

        Assert.Equal("refs/heads/profile-a", await _ws.HeadRefAsync(repo));
        Assert.True(File.Exists(_ws.FileOf(repo, "a-only.txt")));
        Assert.False(File.Exists(_ws.FileOf(repo, "b-only.txt")));
        AssertNamedFilesAsTheyWere(repo);
        Assert.True((await _ws.Engine.GetStatusAsync(repo, Ct)).IsClean);
    }

    [Theory]
    [InlineData("repeat")]
    [InlineData("discard")]
    public async Task A_switch_stopped_part_way_leaves_the_files_the_patterns_name_as_they_were_when_it_is_repeated_or_discarded(string then)
    {
        var (repo, tips) = await NamedProfilesAsync();
        _ws.OnInvoke = invocation =>
        {
            if (invocation.Arguments[0] == "checkout")
            {
                throw new InvalidOperationException("the switch was stopped");
            }
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct));
        _ws.OnInvoke = null;
        Assert.Equal(new InterruptedSwitch(tips.B, tips.A, "profile-a"), SwitchJournal.Read(repo.GitDir));
        AssertNamedFilesAsTheyWere(repo);

        if (then == "repeat")
        {
            await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);
            Assert.Equal("refs/heads/profile-a", await _ws.HeadRefAsync(repo));
            Assert.True(File.Exists(_ws.FileOf(repo, "a-only.txt")));
        }
        else
        {
            await _ws.Engine.DiscardChangesAsync(repo, Ct);
            Assert.Equal("refs/heads/profile-b", await _ws.HeadRefAsync(repo));
            Assert.True(File.Exists(_ws.FileOf(repo, "b-only.txt")));
        }

        AssertNamedFilesAsTheyWere(repo);
        Assert.Null(SwitchJournal.Read(repo.GitDir));
        Assert.True((await _ws.Engine.GetStatusAsync(repo, Ct)).IsClean);
    }

    [Fact]
    public async Task Repeating_a_switch_that_stopped_after_head_moved_does_not_put_back_the_files_the_patterns_name()
    {
        var (repo, tips) = await NamedProfilesAsync();
        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);
        SwitchJournal.Write(repo.GitDir, tips.B, tips.A, "profile-a");

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);

        AssertNamedFilesAsTheyWere(repo);
        Assert.Null(SwitchJournal.Read(repo.GitDir));
        Assert.True((await _ws.Engine.GetStatusAsync(repo, Ct)).IsClean);
    }

    [Fact]
    public async Task A_switch_git_refuses_after_the_check_leaves_the_files_the_patterns_name_out_of_the_status()
    {
        var (repo, _) = await NamedProfilesAsync();
        _ws.OnInvoke = invocation =>
        {
            if (invocation.Arguments[0] == "checkout")
            {
                _ws.Write(repo, "b-only.txt", "edited at the last moment");
            }
        };

        await Assert.ThrowsAsync<DirtyWorkingTreeException>(() => _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct));

        _ws.OnInvoke = null;
        var status = await _ws.Engine.GetStatusAsync(repo, Ct);
        Assert.Equal(["b-only.txt"], status.ChangedPaths);
        Assert.Null(status.InterruptedSwitch);
        AssertNamedFilesAsTheyWere(repo);
    }
    // profile-a: game.cfg, a-only.txt, logs/run.log, cache/shared.bin; profile-b: game.cfg, b-only.txt, logs/b-only.log, logs/b-same.log
    // and its own cache/shared.bin. The folder is on profile-b, the game's patterns are "logs/" and "cache/", and the folder holds the
    // player's own copies of the files those patterns name: logs/b-only.log and cache/shared.bin differ from what profile-b recorded,
    // logs/b-same.log is as profile-b recorded it, and logs/run.log is absent.
    private async Task<(RepoRef Repo, (string A, string B) Tips)> NamedProfilesAsync()
    {
        var (repo, tips) = await RepoTemplates.CopyAsync("switch-safety-named", _ws, async ws =>
        {
            var repo = await ws.InitializedFreshAsync();
            ws.Write(repo, "game.cfg", "cfg");
            ws.Write(repo, "a-only.txt", "a");
            ws.Write(repo, "logs/run.log", "a's log");
            ws.Write(repo, "cache/shared.bin", "a's cache");
            var a = (await ws.CaptureAsync(repo, "profile-a", "A")).TipCommitId!;
            await ws.Engine.CreateBranchAsync(repo, "profile-b", "profile-a", TestContext.Current.CancellationToken);
            await ws.Engine.CheckoutBranchAsync(repo, "profile-b", ct: TestContext.Current.CancellationToken);
            ws.Delete(repo, "a-only.txt");
            ws.Delete(repo, "logs/run.log");
            ws.Write(repo, "b-only.txt", "b");
            ws.Write(repo, "logs/b-only.log", "b's log");
            ws.Write(repo, "logs/b-same.log", "b's log, as the player left it");
            ws.Write(repo, "cache/shared.bin", "b's cache");
            var b = (await ws.CaptureAsync(repo, "profile-b", "B")).TipCommitId!;
            return (repo, (a, b));
        });
        _ws.Write(repo, "logs/b-only.log", "the player's own b log");
        _ws.Write(repo, "cache/shared.bin", "the player's own cache");
        return (repo with { IgnorePatterns = ["logs/", "cache/"] }, tips);
    }
    // What the player's copies held before the switch, whatever the switch, its repeat or a discard did.
    private void AssertNamedFilesAsTheyWere(RepoRef repo)
    {
        Assert.Equal("the player's own b log", File.ReadAllText(_ws.FileOf(repo, "logs/b-only.log")));
        Assert.Equal("the player's own cache", File.ReadAllText(_ws.FileOf(repo, "cache/shared.bin")));
        Assert.Equal("b's log, as the player left it", File.ReadAllText(_ws.FileOf(repo, "logs/b-same.log")));
        Assert.False(File.Exists(_ws.FileOf(repo, "logs/run.log")));
    }
}
