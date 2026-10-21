using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// After a switch the engine removes a file the old point held and git left behind (a file another program had open). It must
// only ever remove exactly that file, never one the player has made or changed since.
[Collection(EngineCollections.F)]
public sealed class LeftoverTests : IDisposable
{
    private readonly EngineWorkspace _ws = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    // profile-1 holds keep/user.ini; profile-2, made from it, does not. The folder is on profile-2 and a stopped switch to it is on record.
    private async Task<RepoRef> HalfSwitchedAsync()
    {
        var (repo, tips) = await RepoTemplates.CopyAsync("leftovers", _ws, async ws =>
        {
            var template = await ws.InitializedFreshAsync();
            ws.Write(template, "keep/user.ini", "v1 of the settings");
            ws.Write(template, "base.txt", "base");
            var one = (await ws.CaptureAsync(template, "profile-1", "One")).TipCommitId!;
            await ws.Engine.CreateBranchAsync(template, "profile-2", "profile-1", TestContext.Current.CancellationToken);
            await ws.Engine.CheckoutBranchAsync(template, "profile-2", ct: TestContext.Current.CancellationToken);
            ws.Delete(template, "keep/user.ini");
            var two = (await ws.CaptureAsync(template, "profile-2", "Two")).TipCommitId!;
            return (template, (one, two));
        });
        SwitchJournal.Write(repo.GitDir, tips.Item1, tips.Item2);
        return repo;
    }

    [Fact]
    public async Task A_file_the_player_made_since_is_kept_when_the_stopped_switch_is_repeated()
    {
        var repo = await HalfSwitchedAsync();
        _ws.Write(repo, "keep/user.ini", "the player's own settings, written later");

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-2", ct: Ct);

        Assert.Equal("the player's own settings, written later", await File.ReadAllTextAsync(_ws.FileOf(repo, "keep/user.ini"), Ct));
        Assert.Null(SwitchJournal.Read(repo.GitDir));
    }

    [Fact]
    public async Task A_file_with_the_bytes_the_old_point_stored_is_removed_when_the_stopped_switch_is_repeated()
    {
        var repo = await HalfSwitchedAsync();
        _ws.Write(repo, "keep/user.ini", "v1 of the settings");

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-2", ct: Ct);

        Assert.False(File.Exists(_ws.FileOf(repo, "keep/user.ini")));
        Assert.Null(SwitchJournal.Read(repo.GitDir));
    }

    [Fact]
    public async Task A_file_behind_a_folder_that_became_a_link_is_not_removed_through_it()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows refuses folder links before a switch, and writes a link as a text file");
        var repo = await _ws.InitializedRepoAsync();
        var outside = Path.Combine(_ws.Root, "outside", "foo-target");
        Directory.CreateDirectory(outside);
        await File.WriteAllTextAsync(Path.Combine(outside, "x.txt"), "secret", Ct);
        _ws.Write(repo, "foo/x.txt", "secret");
        var first = (await _ws.CaptureAsync(repo, "profile-1", "A real folder")).TipCommitId!;

        // A later point holds foo as a link to the outside folder, which has a file with the very bytes the first point stored under foo.
        async Task<string> RunAsync(string? stdin, params string[] arguments) =>
            (await _ws.Runner.RunAsync(new GitInvocation(arguments) { GitDir = repo.GitDir, WorkTree = repo.WorkTree, Stdin = stdin }, Ct)).StandardOutput.Trim();
        var blob = await RunAsync(outside, "hash-object", "-w", "--stdin");
        var tree = await RunAsync($"120000 blob {blob}\tfoo\n", "mktree");
        var commit = await RunAsync("A link\n", "commit-tree", tree, "-p", first, "-F", "-");
        await RunAsync(null, "update-ref", "refs/heads/profile-link", commit);

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-link", ct: Ct);

        Assert.Equal("secret", await File.ReadAllTextAsync(Path.Combine(outside, "x.txt"), Ct));
        Assert.Equal("refs/heads/profile-link", await _ws.HeadRefAsync(repo));
    }

    [Fact]
    public async Task Points_that_hold_one_file_under_names_that_differ_only_in_case_can_be_switched_between()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS(), "this file system tells names apart by case");
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "Data/Foo.esp", "v1");
        var first = (await _ws.CaptureAsync(repo, "profile-1", "With Foo")).TipCommitId!;
        _ws.Delete(repo, "Data/Foo.esp");
        await _ws.CaptureAsync(repo, "profile-1", "Without it");
        _ws.Write(repo, "Data/foo.esp", "v2");
        var third = (await _ws.CaptureAsync(repo, "profile-1", "With foo")).TipCommitId!;

        await _ws.Engine.CheckoutCommitAsync(repo, first, ct: Ct);

        Assert.Equal("v1", await File.ReadAllTextAsync(_ws.FileOf(repo, "Data/Foo.esp"), Ct));
        Assert.Null((await _ws.Engine.GetStatusAsync(repo, Ct)).InterruptedSwitch);

        await _ws.Engine.CheckoutCommitAsync(repo, third, ct: Ct);

        Assert.Equal("v2", await File.ReadAllTextAsync(_ws.FileOf(repo, "Data/foo.esp"), Ct));
        Assert.True((await _ws.Engine.GetStatusAsync(repo, Ct)).IsClean);
    }
}
