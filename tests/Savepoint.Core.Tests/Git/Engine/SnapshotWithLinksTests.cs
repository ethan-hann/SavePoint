using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// A snapshot taken while a mod manager linked its files into the game folder (or on Linux or macOS) holds a file as a symbolic link
// (git mode 120000). Git for Windows (core.symlinks off) writes such an entry back as a text file holding the link's path, so a switch to
// the snapshot, or a discard that would restore the entry, is refused before anything is written. Elsewhere git restores the link.
[Collection(EngineCollections.D)]
public sealed class SnapshotWithLinksTests : IDisposable
{
    private const string _stub = "../outside/linked.esp";

    private readonly EngineWorkspace _ws = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    private static async Task<string> RunAsync(EngineWorkspace ws, RepoRef repo, string? stdin, params string[] arguments) =>
        (await ws.Runner.RunAsync(new GitInvocation(arguments) { GitDir = repo.GitDir, WorkTree = repo.WorkTree, Stdin = stdin }, TestContext.Current.CancellationToken)).StandardOutput.Trim();

    // profile-a holds Mods/a.esp and game.cfg as plain files. profile-links holds the same plus Mods/linked.esp as a link (mode 120000).
    // The folder is on profile-a.
    private async Task<RepoRef> LinkProfilesAsync()
    {
        var (repo, _) = await RepoTemplates.CopyAsync("snapshot-with-links", _ws, async ws =>
        {
            var built = await ws.InitializedFreshAsync();
            ws.Write(built, "Mods/a.esp", "plugin a");
            ws.Write(built, "game.cfg", "cfg");
            var tip = (await ws.CaptureAsync(built, "profile-a", "A")).TipCommitId!;
            var plugin = await RunAsync(ws, built, null, "rev-parse", "profile-a:Mods/a.esp");
            var cfg = await RunAsync(ws, built, null, "rev-parse", "profile-a:game.cfg");
            var link = await RunAsync(ws, built, _stub, "hash-object", "-w", "--stdin");
            var mods = await RunAsync(ws, built, $"100644 blob {plugin}\ta.esp\n120000 blob {link}\tlinked.esp\n", "mktree");
            var root = await RunAsync(ws, built, $"100644 blob {cfg}\tgame.cfg\n040000 tree {mods}\tMods\n", "mktree");
            var commit = await RunAsync(ws, built, "With a link\n", "commit-tree", root, "-p", tip, "-F", "-");
            await RunAsync(ws, built, null, "update-ref", "refs/heads/profile-links", commit);
            return (built, 0);
        });
        return repo;
    }

    // The folder as a switch to profile-links would leave it on Windows: HEAD and the index on profile-links, the link written as text.
    private async Task<RepoRef> OnTheLinkProfileAsync()
    {
        var repo = await LinkProfilesAsync();
        await _ws.GitWriteAsync(repo, "symbolic-ref", "HEAD", "refs/heads/profile-links");
        await _ws.GitWriteAsync(repo, "reset", "-q");
        _ws.Write(repo, "Mods/linked.esp", _stub);
        Assert.True((await _ws.Engine.GetStatusAsync(repo, Ct)).IsClean, "the set-up did not leave the folder clean");
        return repo;
    }

    [Fact]
    public async Task Asking_whether_a_switch_is_possible_refuses_a_snapshot_that_holds_a_link_and_names_the_file()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows writes a link back as a text file");
        var repo = await LinkProfilesAsync();

        var error = await Assert.ThrowsAsync<SnapshotHasLinksException>(() => _ws.Engine.CheckSwitchAsync(repo, "profile-links", isBranch: true, Ct));

        Assert.Equal("error.switch.snapshotHasLinks", error.MessageId);
        Assert.Equal(_ws.FileOf(repo, "Mods/linked.esp"), error.Args["path"]);
        Assert.Equal("linked.esp", error.Args["name"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_switch_to_a_snapshot_that_holds_a_link_is_refused_before_anything_is_written(bool force)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows writes a link back as a text file");
        var repo = await LinkProfilesAsync();

        var switching = force
            ? _ws.Engine.ForceCheckoutBranchAsync(repo, "profile-links", Ct)
            : _ws.Engine.CheckoutBranchAsync(repo, "profile-links", ct: Ct);
        await Assert.ThrowsAsync<SnapshotHasLinksException>(() => switching);

        Assert.False(File.Exists(_ws.FileOf(repo, "Mods/linked.esp")));
        Assert.Equal("refs/heads/profile-a", await _ws.HeadRefAsync(repo));
        Assert.Null(SwitchJournal.Read(repo.GitDir));
    }

    [Fact]
    public async Task A_switch_away_from_a_snapshot_that_holds_a_link_is_not_refused()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows writes a link back as a text file");
        var repo = await OnTheLinkProfileAsync();

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);

        Assert.False(File.Exists(_ws.FileOf(repo, "Mods/linked.esp")));
        Assert.Equal("refs/heads/profile-a", await _ws.HeadRefAsync(repo));
    }

    [Fact]
    public async Task A_discard_that_would_restore_a_link_as_a_text_file_is_refused_and_writes_nothing()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows writes a link back as a text file");
        var repo = await OnTheLinkProfileAsync();
        _ws.Delete(repo, "Mods/linked.esp");
        _ws.Write(repo, "Mods/a.esp", "edited");

        var error = await Assert.ThrowsAsync<SnapshotHasLinksException>(() => _ws.Engine.DiscardChangesAsync(repo, Ct));

        Assert.Equal(_ws.FileOf(repo, "Mods/linked.esp"), error.Args["path"]);
        Assert.False(File.Exists(_ws.FileOf(repo, "Mods/linked.esp")));
        Assert.Equal("edited", File.ReadAllText(_ws.FileOf(repo, "Mods/a.esp")));
    }

    [Fact]
    public async Task A_discard_leaves_alone_a_link_the_folder_already_holds_as_the_text_a_switch_wrote()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows writes a link back as a text file");
        var repo = await OnTheLinkProfileAsync();
        _ws.Write(repo, "Mods/a.esp", "edited");

        await _ws.Engine.DiscardChangesAsync(repo, Ct);

        Assert.Equal("plugin a", File.ReadAllText(_ws.FileOf(repo, "Mods/a.esp")));
        Assert.Equal(_stub, File.ReadAllText(_ws.FileOf(repo, "Mods/linked.esp")));
    }

    [Fact]
    public async Task A_switch_to_a_snapshot_that_holds_a_link_restores_the_link_where_git_stores_links()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows refuses such a snapshot instead");
        var repo = await LinkProfilesAsync();

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-links", ct: Ct);

        Assert.Equal(_stub, new FileInfo(_ws.FileOf(repo, "Mods/linked.esp")).LinkTarget);
    }
}
