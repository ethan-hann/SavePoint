using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// A junction (or folder link) inside the game folder leads out of it; git for Windows goes through it as if it were a
// folder, so a switch or a discard would overwrite and delete files that belong to something else.
[Collection(EngineCollections.D)]
public sealed class LinkedFolderTests : IDisposable
{
    private readonly EngineWorkspace _ws = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    private string Outside(string name) => Path.Combine(_ws.Root, "outside", name);

    // Replaces the folder with a link to another one; skips the test where a link cannot be made.
    private void ReplaceWithLink(RepoRef repo, string folder, string target)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "folder links are only refused on Windows");
        var path = _ws.FileOf(repo, folder);
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }

        Directory.CreateDirectory(target);
        Assert.SkipUnless(TestLinks.TryCreateFolderLink(path, target), "this system cannot make folder links");
    }

    private static string[] OutsideFiles(string folder) =>
        [.. Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Select(file => Path.GetRelativePath(folder, file) + "=" + File.ReadAllText(file))];

    // Mods/a.esp is in profile-a and not in profile-b; the folder is on profile-b with Mods a real folder.
    private Task<(RepoRef Repo, (string A, string B) Tips)> ModsProfilesAsync() =>
        RepoTemplates.CopyAsync("linked-folders", _ws, async ws =>
        {
            var repo = await ws.InitializedFreshAsync();
            ws.Write(repo, "Mods/a.esp", "plugin a");
            ws.Write(repo, "game.cfg", "cfg");
            var a = (await ws.CaptureAsync(repo, "profile-a", "A")).TipCommitId!;
            await ws.Engine.CreateBranchAsync(repo, "profile-b", "profile-a", TestContext.Current.CancellationToken);
            await ws.Engine.CheckoutBranchAsync(repo, "profile-b", ct: TestContext.Current.CancellationToken);
            ws.Delete(repo, "Mods/a.esp");
            var b = (await ws.CaptureAsync(repo, "profile-b", "B")).TipCommitId!;
            return (repo, (a, b));
        });

    [Fact]
    public async Task A_capture_refuses_a_junction_inside_the_game_folder_and_reads_nothing_through_it()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "game.cfg", "cfg");
        ReplaceWithLink(repo, "Mods", Outside("staging"));
        File.WriteAllText(Path.Combine(Outside("staging"), "precious.esp"), "not part of this game");

        var error = await Assert.ThrowsAsync<LinkedFolderException>(() => _ws.CaptureAsync(repo, "profile-1"));

        Assert.Equal("error.capture.linkedFolder", error.MessageId);
        Assert.Equal(_ws.FileOf(repo, "Mods"), error.Args["path"]);
        Assert.DoesNotContain(_ws.Invocations, invocation => invocation.Arguments.Contains("add") || invocation.Arguments.Contains("commit"));
        Assert.Null(await _ws.TipAsync(repo, "profile-1"));
    }

    [Fact]
    public async Task The_capture_candidates_refuse_it_too()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "game.cfg", "cfg");
        ReplaceWithLink(repo, "Mods", Outside("staging"));
        File.WriteAllText(Path.Combine(Outside("staging"), "precious.esp"), "x");

        var error = await Assert.ThrowsAsync<LinkedFolderException>(() => _ws.Engine.ListCaptureCandidatesAsync(repo, [], Ct));

        Assert.Equal(_ws.FileOf(repo, "Mods"), error.Args["path"]);
    }

    [Fact]
    public async Task A_switch_that_would_write_through_a_junction_is_refused_and_writes_nothing()
    {
        var (repo, _) = await ModsProfilesAsync();
        ReplaceWithLink(repo, "Mods", Outside("staging"));

        await Assert.ThrowsAsync<LinkedFolderException>(() => _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct));

        Assert.Empty(OutsideFiles(Outside("staging")));
        Assert.Equal("refs/heads/profile-b", await _ws.HeadRefAsync(repo));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_switch_that_would_delete_through_a_junction_is_refused_and_deletes_nothing(bool force)
    {
        var (repo, _) = await ModsProfilesAsync();
        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);
        ReplaceWithLink(repo, "Mods", Outside("staging"));
        File.WriteAllText(Path.Combine(Outside("staging"), "a.esp"), "plugin a");

        var switching = force
            ? _ws.Engine.ForceCheckoutBranchAsync(repo, "profile-b", Ct)
            : _ws.Engine.CheckoutBranchAsync(repo, "profile-b", ct: Ct);
        await Assert.ThrowsAsync<LinkedFolderException>(() => switching);

        Assert.Equal(["a.esp=plugin a"], OutsideFiles(Outside("staging")));
    }

    [Fact]
    public async Task Discard_that_would_overwrite_and_clean_that_would_delete_through_a_junction_are_refused()
    {
        var (repo, _) = await ModsProfilesAsync();
        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);
        ReplaceWithLink(repo, "Mods", Outside("shared"));
        File.WriteAllText(Path.Combine(Outside("shared"), "a.esp"), "a newer plugin that belongs to another game");
        File.WriteAllText(Path.Combine(Outside("shared"), "other-game-mod.esp"), "another game's mod");
        var before = OutsideFiles(Outside("shared"));

        await Assert.ThrowsAsync<LinkedFolderException>(() => _ws.Engine.DiscardChangesAsync(repo, Ct));
        await Assert.ThrowsAsync<LinkedFolderException>(() => _ws.Engine.CleanAsync(repo, Ct));

        Assert.Equal(before, OutsideFiles(Outside("shared")));
    }

    [Fact]
    public async Task A_junction_that_holds_nothing_the_operation_touches_is_not_in_the_way()
    {
        var (repo, _) = await ModsProfilesAsync();
        ReplaceWithLink(repo, "Cache", Outside("cache"));

        var outcome = await _ws.CaptureAsync(repo, "profile-b");

        Assert.False(outcome.Changed);
    }

    [Fact]
    public async Task A_link_in_a_game_folder_is_stored_as_a_link_where_that_is_what_git_does()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows refuses folder links instead");
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "game.cfg", "cfg");
        var target = Outside("lib");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "libgame.so"), "library");
        Assert.SkipUnless(TestLinks.TryCreateFolderLink(_ws.FileOf(repo, "lib"), target), "this system cannot make folder links");

        await _ws.CaptureAsync(repo, "profile-1");

        var tree = await _ws.GitOutAsync(repo, "ls-tree", "-r", "profile-1");
        Assert.Contains("120000 blob", tree, StringComparison.Ordinal);
        Assert.DoesNotContain("libgame.so", tree, StringComparison.Ordinal);
    }

    [Fact]
    public void A_folder_inside_a_link_is_told_from_one_that_is_not()
    {
        var work = Path.Combine(_ws.Root, "linkwork");
        var target = Outside("linkdest");
        Directory.CreateDirectory(Path.Combine(work, "plain", "deeper"));
        Directory.CreateDirectory(target);
        Assert.SkipUnless(TestLinks.TryCreateFolderLink(Path.Combine(work, "Linked"), target), "this system cannot make folder links");

        Assert.True(LinkedFolders.IsInsideLink(work, "Linked/x.txt"));
        Assert.True(LinkedFolders.IsInsideLink(work, "Linked/sub/x.txt"));
        Assert.False(LinkedFolders.IsInsideLink(work, "plain/deeper/x.txt"));
        Assert.False(LinkedFolders.IsInsideLink(work, "top.txt"));
        TestLinks.RemoveFolderLink(Path.Combine(work, "Linked"));
    }
}
