using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// A target that holds a link to a folder (git mode 120000, such as lib -> lib64 in a native Linux game) where the point being left holds
// a real folder. A switch killed after it made the link leaves the link standing where the old folder was, and finishing the switch has
// to replace what the old point owned there with the link without ever going through it.
[Collection(EngineCollections.A)]
public sealed class FinishSwitchFolderLinkTests : IDisposable
{
    private readonly EngineWorkspace _ws = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    private string Outside(string name) => Path.Combine(_ws.Root, "outside", name);

    private static void SkipWhereLinksAreNotStoredAsLinks() =>
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows refuses folder links instead, and writes a link as a text file");

    // Profile-old holds lib as a folder with x.so in it. Profile-new holds lib as a link to linkTarget. The folder is on profile-old.
    private async Task<(RepoRef Repo, string Old, string New)> ProfilesAsync(string linkTarget)
    {
        var repo = await _ws.InitializedFreshAsync();
        _ws.Write(repo, "lib/x.so", "the old library");
        _ws.Write(repo, "lib64/y.so", "the 64-bit library");
        var old = (await _ws.CaptureAsync(repo, "profile-old", "Old")).TipCommitId!;
        await _ws.Engine.CreateBranchAsync(repo, "profile-new", "profile-old", Ct);
        await _ws.Engine.CheckoutBranchAsync(repo, "profile-new", ct: Ct);
        _ws.Delete(repo, "lib/x.so");
        Directory.Delete(_ws.FileOf(repo, "lib"));
        Directory.CreateSymbolicLink(_ws.FileOf(repo, "lib"), linkTarget);
        var made = (await _ws.CaptureAsync(repo, "profile-new", "New")).TipCommitId!;
        await _ws.Engine.CheckoutBranchAsync(repo, "profile-old", ct: Ct);
        Assert.True(File.Exists(_ws.FileOf(repo, "lib/x.so")), "the set-up did not put the old folder back");
        return (repo, old, made);
    }

    // What a switch to profile-new leaves when it is killed after it removed the old folder and made the link: HEAD and the index still say profile-old.
    private void KillTheSwitchAfterTheLink(RepoRef repo, string old, string made, string linkTarget)
    {
        SwitchJournal.Write(repo.GitDir, old, made, "profile-new");
        _ws.Delete(repo, "lib/x.so");
        Directory.Delete(_ws.FileOf(repo, "lib"));
        Directory.CreateSymbolicLink(_ws.FileOf(repo, "lib"), linkTarget);
    }

    [Fact]
    public async Task A_switch_killed_after_it_made_the_link_where_the_old_folder_was_is_finished()
    {
        SkipWhereLinksAreNotStoredAsLinks();
        var (repo, old, made) = await ProfilesAsync("lib64");
        KillTheSwitchAfterTheLink(repo, old, made, "lib64");

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-new", ct: Ct);

        Assert.Equal("lib64", new FileInfo(_ws.FileOf(repo, "lib")).LinkTarget);
        Assert.Equal("the 64-bit library", File.ReadAllText(_ws.FileOf(repo, "lib64/y.so")));
        Assert.Equal("refs/heads/profile-new", await _ws.HeadRefAsync(repo));
        Assert.Null(SwitchJournal.Read(repo.GitDir));
        Assert.True((await _ws.Engine.GetStatusAsync(repo, Ct)).IsClean);
    }

    [Fact]
    public async Task Finishing_removes_the_link_and_never_what_it_leads_to()
    {
        SkipWhereLinksAreNotStoredAsLinks();
        var outside = Outside("lib64");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "precious.so"), "not part of this game");
        var (repo, old, made) = await ProfilesAsync(outside);
        KillTheSwitchAfterTheLink(repo, old, made, outside);

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-new", ct: Ct);

        Assert.Equal(outside, new FileInfo(_ws.FileOf(repo, "lib")).LinkTarget);
        Assert.Equal(["precious.so"], Directory.EnumerateFileSystemEntries(outside).Select(Path.GetFileName));
        Assert.Equal("not part of this game", File.ReadAllText(Path.Combine(outside, "precious.so")));
        Assert.True((await _ws.Engine.GetStatusAsync(repo, Ct)).IsClean);
    }

    [Fact]
    public async Task The_review_does_not_list_the_link_the_killed_switch_made_as_replaced()
    {
        SkipWhereLinksAreNotStoredAsLinks();
        var (repo, old, made) = await ProfilesAsync("lib64");
        KillTheSwitchAfterTheLink(repo, old, made, "lib64");

        var review = await _ws.Engine.ReviewStoppedSwitchAsync(repo, Ct);

        Assert.Empty(review.ReplacedPaths);
        Assert.Empty(review.BlockingPaths);
    }

    [Fact]
    public async Task A_link_pointing_somewhere_else_is_listed_as_replaced_and_the_finish_puts_the_targets_link_there()
    {
        SkipWhereLinksAreNotStoredAsLinks();
        var (repo, old, made) = await ProfilesAsync("lib64");
        KillTheSwitchAfterTheLink(repo, old, made, "somewhere-else-entirely");

        var review = await _ws.Engine.ReviewStoppedSwitchAsync(repo, Ct);
        await _ws.Engine.CheckoutBranchAsync(repo, "profile-new", ct: Ct);

        Assert.Equal(["lib"], review.ReplacedPaths);
        Assert.Equal("lib64", new FileInfo(_ws.FileOf(repo, "lib")).LinkTarget);
        Assert.True((await _ws.Engine.GetStatusAsync(repo, Ct)).IsClean);
    }

    [Fact]
    public async Task A_file_the_player_put_where_the_old_folder_was_is_listed_as_replaced_and_the_finish_puts_the_link_there()
    {
        SkipWhereLinksAreNotStoredAsLinks();
        var (repo, old, made) = await ProfilesAsync("lib64");
        SwitchJournal.Write(repo.GitDir, old, made, "profile-new");
        _ws.Delete(repo, "lib/x.so");
        Directory.Delete(_ws.FileOf(repo, "lib"));
        _ws.Write(repo, "lib", "the player's own file, made after the switch stopped");

        var review = await _ws.Engine.ReviewStoppedSwitchAsync(repo, Ct);
        await _ws.Engine.CheckoutBranchAsync(repo, "profile-new", ct: Ct);

        Assert.Equal(["lib"], review.ReplacedPaths);
        Assert.Equal("lib64", new FileInfo(_ws.FileOf(repo, "lib")).LinkTarget);
        Assert.True((await _ws.Engine.GetStatusAsync(repo, Ct)).IsClean);
    }
}
