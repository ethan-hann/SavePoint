using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// A file git has just written carries a time stamp in the same second as the index, so git cannot trust the stamp and reads the file
// again on every later comparison until the index is rewritten. A big file made every status, every dirty check and the next switch
// wait for a full read, so a switch and a discard rewrite the index once, before they verify.
[Collection(EngineCollections.A)]
public sealed class IndexRefreshTests : IDisposable
{
    private readonly EngineWorkspace _ws = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    [Fact]
    public async Task A_switch_rewrites_the_index_after_git_wrote_the_files_and_before_it_verifies_them()
    {
        var repo = await TwoProfilesAsync();
        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);
        var before = _ws.Invocations.Count;

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-b", ct: Ct);

        var calls = _ws.Invocations.Skip(before).ToList();
        var written = calls.FindIndex(call => call.Arguments[0] == "checkout" && call.Arguments.Contains("profile-b"));
        var refreshed = calls.FindIndex(IsRefresh);
        var verified = calls.FindIndex(EngineWorkspace.IsVerifyDiff);
        Assert.True(written >= 0 && refreshed > written, "expected the index to be refreshed after git wrote the files");
        Assert.True(verified > refreshed, "expected the index to be refreshed before the verify pass reads the folder");
    }

    [Fact]
    public async Task A_switch_that_only_removes_files_also_refreshes_the_index()
    {
        var repo = await TwoProfilesAsync();
        var before = _ws.Invocations.Count;

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);

        Assert.Single(_ws.Invocations.Skip(before), IsRefresh);
    }

    [Fact]
    public async Task A_switch_to_an_older_point_refreshes_the_index()
    {
        var (repo, points) = await _ws.ThreePointsRepoAsync();
        var before = _ws.Invocations.Count;

        await _ws.Engine.CheckoutCommitAsync(repo, points[0], ct: Ct);

        Assert.Single(_ws.Invocations.Skip(before), IsRefresh);
    }

    [Fact]
    public async Task A_discard_rewrites_the_index_after_it_restored_the_files_and_before_it_verifies_them()
    {
        var repo = await TwoProfilesAsync();
        _ws.Write(repo, "Data/blob.bin", "edited by the player");
        var before = _ws.Invocations.Count;

        await _ws.Engine.DiscardChangesAsync(repo, Ct);

        var calls = _ws.Invocations.Skip(before).ToList();
        var restored = calls.FindIndex(call => call.Arguments[0] == "reset" && call.Arguments.Contains("--hard"));
        var refreshed = calls.FindIndex(IsRefresh);
        var verified = calls.FindIndex(EngineWorkspace.IsVerifyDiff);
        Assert.True(restored >= 0 && refreshed > restored, "expected the index to be refreshed after the files were restored");
        Assert.True(verified > refreshed, "expected the index to be refreshed before the verify pass reads the folder");
    }

    [Fact]
    public async Task A_switch_that_is_refused_before_it_starts_does_not_refresh_the_index()
    {
        var repo = await TwoProfilesAsync();
        _ws.Write(repo, "Data/blob.bin", "edited by the player");
        var before = _ws.Invocations.Count;

        await Assert.ThrowsAsync<DirtyWorkingTreeException>(() => _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct));

        Assert.DoesNotContain(_ws.Invocations.Skip(before), IsRefresh);
    }

    [Fact]
    public async Task A_file_the_verify_pass_has_to_write_again_is_followed_by_another_refresh_before_the_next_look()
    {
        var repo = await TwoProfilesAsync();
        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);
        var stale = _ws.FileOf(repo, "Data/extra.pak");
        var corrupted = false;
        _ws.OnInvoke = call =>
        {
            if (EngineWorkspace.IsVerifyDiff(call) && !corrupted)
            {
                corrupted = true;
                File.WriteAllText(stale, "garbage from a failed write");
            }
        };
        var before = _ws.Invocations.Count;

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-b", ct: Ct);

        var calls = _ws.Invocations.Skip(before).ToList();
        var resync = calls.FindIndex(call => call.Arguments[0] == "checkout" && call.Arguments.Contains("--pathspec-from-file=-"));
        var secondVerify = calls.FindLastIndex(EngineWorkspace.IsVerifyDiff);
        Assert.True(resync >= 0 && secondVerify > resync, "expected the verify pass to write the file again and look again");
        Assert.Contains(calls.Skip(resync).Take(secondVerify - resync), IsRefresh);
    }

    private static bool IsRefresh(GitInvocation call) => call.Arguments[0] == "update-index" && call.Arguments.Contains("--refresh");

    // profile-b holds everything profile-a does plus a second file; the folder ends on profile-b.
    private async Task<RepoRef> TwoProfilesAsync()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "Data/blob.bin", "the base game");
        await _ws.CaptureAsync(repo, "profile-a");
        await _ws.GitWriteAsync(repo, "checkout", "--quiet", "-b", "profile-b");
        _ws.Write(repo, "Data/extra.pak", "an addition");
        await _ws.CaptureAsync(repo, "profile-b");
        return repo;
    }
}
