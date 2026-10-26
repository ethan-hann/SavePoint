using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// On a drive that ignores case a file the switch renames only in case (and changes) is the same file to the system, so a stopped switch
// leaves one file under the new spelling while the index still holds the old one.
[Collection(EngineCollections.F)]
public sealed class FinishSwitchCaseRenameTests : IDisposable
{
    private readonly EngineWorkspace _ws = new(options => options with { SystemIgnoresCase = true });

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    // Profile-a holds Data/mod.pak, profile-b the same file as Data/Mod.pak with other content. The folder is on profile-b.
    private async Task<(RepoRef Repo, (string A, string B, SortedDictionary<string, string> ASnapshot) Data)> ProfilesAsync() =>
        await RepoTemplates.CopyAsync("finish-switch-case-rename", _ws, async ws =>
        {
            var repo = await ws.InitializedFreshAsync();
            await ws.GitWriteAsync(repo, "config", "core.ignorecase", "true");
            ws.Write(repo, "keep.cfg", "cfg");
            ws.Write(repo, "Data/mod.pak", "a's pack");
            var a = (await ws.CaptureAsync(repo, "profile-a", "A")).TipCommitId!;
            var snapshot = TreeSnapshot.Take(repo.WorkTree);
            await ws.Engine.CreateBranchAsync(repo, "profile-b", "profile-a", TestContext.Current.CancellationToken);
            await ws.Engine.CheckoutBranchAsync(repo, "profile-b", ct: TestContext.Current.CancellationToken);

            // The index has to forget the old spelling, or a drive that ignores case keeps recording the file under it.
            await ws.GitWriteAsync(repo, "rm", "--cached", "-q", "Data/mod.pak");
            ws.Delete(repo, "Data/mod.pak");
            ws.Write(repo, "Data/Mod.pak", "b's pack");
            var b = (await ws.CaptureAsync(repo, "profile-b", "B")).TipCommitId!;
            return (repo, (a, b, snapshot));
        });

    [Theory]
    [InlineData("a's pack")]
    [InlineData("a's p")]
    [InlineData("")]
    public async Task A_switch_killed_after_it_replaced_a_file_that_differs_only_in_case_is_finished(string written)
    {
        var (repo, data) = await ProfilesAsync();
        SwitchJournal.Write(repo.GitDir, data.B, data.A, "profile-a");
        _ws.Delete(repo, "Data/Mod.pak");
        if (written.Length > 0)
        {
            _ws.Write(repo, "Data/mod.pak", written);
        }

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);

        Assert.Equal("refs/heads/profile-a", await _ws.HeadRefAsync(repo));
        Assert.Null(SwitchJournal.Read(repo.GitDir));
        Assert.Equal(data.ASnapshot, TreeSnapshot.Take(repo.WorkTree));
        Assert.Equal(["Data/mod.pak", "keep.cfg"], (await _ws.GitOutAsync(repo, "ls-files")).Split('\n'));
        Assert.True((await _ws.Engine.GetStatusAsync(repo, Ct)).IsClean);
    }
}
