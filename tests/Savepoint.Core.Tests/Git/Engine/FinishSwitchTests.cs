using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// A switch killed while git writes files leaves HEAD and the index at the old point and the folder part way to the new one. Switching
// again to the same point must finish it without running over anything the player changed outside the files that switch writes.
[Collection(EngineCollections.F)]
public sealed class FinishSwitchTests : IDisposable
{
    private readonly EngineWorkspace _ws = new();

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
    // there already, b-only.txt is gone and logs/b.log is not yet. HEAD and the index still say profile-b.
    private void KillTheSwitchPartWay(RepoRef repo, string a, string b)
    {
        SwitchJournal.Write(repo.GitDir, b, a, "profile-a");
        _ws.Write(repo, "shared.dat", "a's data");
        _ws.Write(repo, "Data/mod.pak", "a's mo");
        _ws.Write(repo, "a-only.txt", "a");
        _ws.Delete(repo, "b-only.txt");
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
    public async Task A_killed_switch_that_stood_a_folder_where_a_file_was_is_finished_too()
    {
        var (repo, data) = await FileAndFolderProfilesAsync();
        SwitchJournal.Write(repo.GitDir, data.B, data.A, "profile-a");

        // The file Mods/Foo is gone and the folder that takes its place already holds half a file.
        _ws.Delete(repo, "Mods/Foo/x.txt");
        Directory.Delete(_ws.FileOf(repo, "Mods/Foo"));
        _ws.Write(repo, "Mods/Foo", "a's file, cut sh");

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);

        Assert.Equal("a's file", File.ReadAllText(_ws.FileOf(repo, "Mods/Foo")));
        Assert.Null(SwitchJournal.Read(repo.GitDir));
        Assert.True((await _ws.Engine.GetStatusAsync(repo, Ct)).IsClean);
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
