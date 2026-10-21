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
        Assert.Equal(new InterruptedSwitch(tips.B, tips.A), SwitchJournal.Read(repo.GitDir));
    }

    [Fact]
    public async Task A_half_switched_folder_refuses_a_switch_to_a_different_point_and_keeps_the_note()
    {
        var (repo, tips) = await TwoProfilesAsync();
        SwitchJournal.Write(repo.GitDir, tips.B, tips.A);

        await Assert.ThrowsAsync<DirtyWorkingTreeException>(() => _ws.Engine.CheckoutBranchAsync(repo, "profile-b", ct: Ct));

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

        var error = await Assert.ThrowsAsync<DirtyWorkingTreeException>(() => _ws.Engine.CaptureAsync(repo, request, null, Ct));

        Assert.Equal(RepoRole.Install, error.Args["role"]);
        Assert.DoesNotContain(_ws.Invocations, invocation => invocation.Arguments.Contains("add") || invocation.Arguments.Contains("commit"));
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
    public async Task A_switch_never_deletes_a_tracked_file_the_games_patterns_now_name()
    {
        var (repo, _) = await LogProfilesAsync();
        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);
        await _ws.CaptureAsync(repo, "profile-a", ignore: ["logs/"]);

        await Assert.ThrowsAsync<DirtyWorkingTreeException>(() => _ws.Engine.CheckoutBranchAsync(repo, "profile-b", ct: Ct));

        Assert.Equal("a's copy", await File.ReadAllTextAsync(_ws.FileOf(repo, "logs/run.log"), Ct));
        Assert.Equal("refs/heads/profile-a", await _ws.HeadRefAsync(repo));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_switch_never_overwrites_the_players_file_the_games_patterns_name_with_an_older_copy(bool force)
    {
        var (repo, _) = await LogProfilesAsync();
        await _ws.CaptureAsync(repo, "profile-b", ignore: ["logs/"]);
        _ws.Write(repo, "logs/run.log", "the player's own log");

        var switching = force
            ? _ws.Engine.ForceCheckoutBranchAsync(repo, "profile-a", Ct)
            : _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);
        await Assert.ThrowsAsync<DirtyWorkingTreeException>(() => switching);

        Assert.Equal("the player's own log", await File.ReadAllTextAsync(_ws.FileOf(repo, "logs/run.log"), Ct));
        Assert.Equal("refs/heads/profile-b", await _ws.HeadRefAsync(repo));
    }

    [Fact]
    public async Task Files_the_games_patterns_name_do_not_stand_in_the_way_of_a_switch_that_only_adds_a_file_beside_them()
    {
        var (repo, _) = await LogProfilesAsync();
        await _ws.CaptureAsync(repo, "profile-b", ignore: ["logs/"]);
        _ws.Write(repo, "logs/other.log", "the player's own log");

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);

        Assert.Equal("the player's own log", await File.ReadAllTextAsync(_ws.FileOf(repo, "logs/other.log"), Ct));
        Assert.Equal("a's copy", await File.ReadAllTextAsync(_ws.FileOf(repo, "logs/run.log"), Ct));
    }
}
