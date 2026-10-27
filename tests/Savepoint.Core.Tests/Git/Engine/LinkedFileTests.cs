using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// A mod manager's link deployment fills a folder with file symbolic links. Git for Windows (core.symlinks off) records a link as the
// text of its target and writes that text back as an ordinary file, so a switch or a discard would replace the link and the deployment
// would stop loading. Elsewhere git stores and restores links natively.
[Collection(EngineCollections.D)]
public sealed class LinkedFileTests : IDisposable
{
    private readonly EngineWorkspace _ws = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    private string Outside(string name) => Path.Combine(_ws.Root, "outside", name);

    // Puts a file link at the path, pointing at a real file elsewhere; skips the test where a file link cannot be made.
    private string Link(RepoRef repo, string file, string content = "the mod manager's copy")
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "file links are only refused on Windows");
        var path = _ws.FileOf(repo, file);
        File.Delete(path);
        var target = Outside(Path.GetFileName(file));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, content);
        Assert.SkipUnless(TestLinks.TryCreateFileLink(path, target), "this system cannot make file links (needs Developer Mode or an elevated account)");
        return target;
    }

    private static bool IsLink(string path) => new FileInfo(path).LinkTarget is not null;

    // Mods/a.esp is in profile-a and not in profile-b; the folder is on profile-b.
    private async Task<RepoRef> ModsProfilesAsync()
    {
        var (repo, _) = await RepoTemplates.CopyAsync("linked-files", _ws, async ws =>
        {
            var built = await ws.InitializedFreshAsync();
            ws.Write(built, "Mods/a.esp", "plugin a");
            ws.Write(built, "game.cfg", "cfg");
            await ws.CaptureAsync(built, "profile-a", "A");
            await ws.Engine.CreateBranchAsync(built, "profile-b", "profile-a", TestContext.Current.CancellationToken);
            await ws.Engine.CheckoutBranchAsync(built, "profile-b", ct: TestContext.Current.CancellationToken);
            ws.Delete(built, "Mods/a.esp");
            await ws.CaptureAsync(built, "profile-b", "B");
            return (built, 0);
        });
        return repo;
    }

    [Fact]
    public async Task A_capture_refuses_a_file_link_and_names_it_and_stages_nothing()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "game.cfg", "cfg");
        _ws.Write(repo, "Mods/real.esp", "a real plugin");
        _ws.Write(repo, "Mods/linked.esp", "placeholder");
        Link(repo, "Mods/linked.esp");

        var error = await Assert.ThrowsAsync<LinkedFileException>(() => _ws.CaptureAsync(repo, "profile-1"));

        Assert.Equal("error.capture.linkedFile", error.MessageId);
        Assert.Equal(_ws.FileOf(repo, "Mods/linked.esp"), error.Args["path"]);
        Assert.Equal("linked.esp", error.Args["name"]);
        Assert.DoesNotContain(_ws.Invocations, invocation => invocation.Arguments.Contains("add") || invocation.Arguments.Contains("commit") || invocation.Arguments.Contains("commit-tree"));
        Assert.Null(await _ws.TipAsync(repo, "profile-1"));
    }

    [Fact]
    public async Task The_capture_candidates_refuse_a_file_link_too()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "game.cfg", "cfg");
        _ws.Write(repo, "Mods/linked.esp", "placeholder");
        Link(repo, "Mods/linked.esp");

        var error = await Assert.ThrowsAsync<LinkedFileException>(() => _ws.Engine.ListCaptureCandidatesAsync(repo, [], Ct));

        Assert.Equal(_ws.FileOf(repo, "Mods/linked.esp"), error.Args["path"]);
    }

    [Fact]
    public async Task A_file_link_the_ignore_list_names_is_not_in_the_way()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "game.cfg", "cfg");
        _ws.Write(repo, "Mods/real.esp", "a real plugin");
        _ws.Write(repo, "Mods/linked.esp", "placeholder");
        Link(repo, "Mods/linked.esp");

        var outcome = await _ws.CaptureAsync(repo, "profile-1", ignore: ["Mods/linked.esp"]);

        Assert.True(outcome.Changed);
        Assert.Equal(["Mods/real.esp", "game.cfg"], (await _ws.GitOutAsync(repo, "ls-tree", "-r", "--name-only", "profile-1")).Split('\n'));
    }

    [Fact]
    public async Task A_folder_symbolic_link_is_still_refused_as_a_linked_folder()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "folder links are only refused on Windows");
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "game.cfg", "cfg");
        var target = Outside("staging");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "precious.esp"), "not part of this game");
        Assert.SkipUnless(TestLinks.TryCreateFolderSymbolicLink(_ws.FileOf(repo, "Mods"), target), "this system cannot make folder symbolic links (needs Developer Mode or an elevated account)");

        var error = await Assert.ThrowsAsync<LinkedFolderException>(() => _ws.CaptureAsync(repo, "profile-1"));

        Assert.Equal(_ws.FileOf(repo, "Mods"), error.Args["path"]);
    }

    [Fact]
    public async Task A_switch_that_would_write_over_a_file_link_is_refused_and_leaves_the_link()
    {
        var repo = await ModsProfilesAsync();
        _ws.Write(repo, "Mods/a.esp", "placeholder");
        var target = Link(repo, "Mods/a.esp");

        await Assert.ThrowsAsync<LinkedFileException>(() => _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct));

        Assert.True(IsLink(_ws.FileOf(repo, "Mods/a.esp")));
        Assert.Equal("the mod manager's copy", File.ReadAllText(target));
        Assert.Equal("refs/heads/profile-b", await _ws.HeadRefAsync(repo));
    }

    [Fact]
    public async Task A_file_link_is_found_whatever_the_case_of_its_name()
    {
        var repo = await ModsProfilesAsync();
        _ws.Write(repo, "Mods/A.ESP", "placeholder");
        Link(repo, "Mods/A.ESP");

        await Assert.ThrowsAsync<LinkedFileException>(() => _ws.Engine.CheckSwitchAsync(repo, "profile-a", isBranch: true, Ct));
    }

    [Fact]
    public async Task A_capture_of_a_folder_whose_recorded_files_are_all_gone_is_not_stopped_by_the_link_check()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "game.cfg", "cfg");
        _ws.Write(repo, "Gone/deeper/old.esp", "old");
        await _ws.CaptureAsync(repo, "profile-1");
        Directory.Delete(_ws.FileOf(repo, "Gone"), recursive: true);

        var outcome = await _ws.CaptureAsync(repo, "profile-1", "Without the folder");

        Assert.True(outcome.Changed);
    }

    [Fact]
    public async Task Asking_whether_a_switch_is_possible_refuses_a_file_link_too()
    {
        var repo = await ModsProfilesAsync();
        _ws.Write(repo, "Mods/a.esp", "placeholder");
        Link(repo, "Mods/a.esp");

        var error = await Assert.ThrowsAsync<LinkedFileException>(() => _ws.Engine.CheckSwitchAsync(repo, "profile-a", isBranch: true, Ct));

        Assert.Equal(_ws.FileOf(repo, "Mods/a.esp"), error.Args["path"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_switch_that_would_delete_a_file_link_is_refused_and_leaves_the_link(bool force)
    {
        var repo = await ModsProfilesAsync();
        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);
        var target = Link(repo, "Mods/a.esp");

        var switching = force
            ? _ws.Engine.ForceCheckoutBranchAsync(repo, "profile-b", Ct)
            : _ws.Engine.CheckoutBranchAsync(repo, "profile-b", ct: Ct);
        await Assert.ThrowsAsync<LinkedFileException>(() => switching);

        Assert.True(IsLink(_ws.FileOf(repo, "Mods/a.esp")));
        Assert.Equal("the mod manager's copy", File.ReadAllText(target));
    }

    [Fact]
    public async Task Discard_that_would_overwrite_and_clean_that_would_delete_a_file_link_are_refused()
    {
        var repo = await ModsProfilesAsync();
        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);
        var changed = Link(repo, "Mods/a.esp");
        _ws.Write(repo, "Mods/new.esp", "placeholder");
        var added = Link(repo, "Mods/new.esp");

        await Assert.ThrowsAsync<LinkedFileException>(() => _ws.Engine.DiscardChangesAsync(repo, Ct));
        await Assert.ThrowsAsync<LinkedFileException>(() => _ws.Engine.CleanAsync(repo, Ct));

        Assert.True(IsLink(_ws.FileOf(repo, "Mods/a.esp")));
        Assert.True(IsLink(_ws.FileOf(repo, "Mods/new.esp")));
        Assert.Equal("the mod manager's copy", File.ReadAllText(changed));
        Assert.Equal("the mod manager's copy", File.ReadAllText(added));
    }

    [Fact]
    public async Task A_file_link_in_a_game_folder_is_stored_as_a_link_where_that_is_what_git_does()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows refuses file links instead");
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "game.cfg", "cfg");
        var target = Outside("libgame.so.1");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, "library");
        Assert.SkipUnless(TestLinks.TryCreateFileLink(_ws.FileOf(repo, "libgame.so"), target), "this system cannot make file links");

        await _ws.CaptureAsync(repo, "profile-1");

        var tree = await _ws.GitOutAsync(repo, "ls-tree", "-r", "profile-1");
        Assert.Contains("120000 blob", tree, StringComparison.Ordinal);
    }
}
