using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// A file the game's patterns name keeps its own bytes through a switch, and its index entry says what the target says, so it never matches
// the folder. git hashes a file whose entry does not match each time something compares the folder with the index; for a big cache that
// is a full read per comparison. Git skips an entry marked skip-worktree in both the index refresh and the verify diff, so the switch
// keeps the mark on the entries of those files while it runs them and takes it off afterwards.
[Collection(EngineCollections.D)]
public sealed class LeftOutReadOnceTests : IDisposable
{
    private const string _cache = "Cache/big.bin";

    private readonly EngineWorkspace _ws = new(options => options with
    {
        ResyncDelays = [TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(20)],
        LockRetryDelays = [TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10)],
    });

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    // Both profiles record the cache, with different bytes, from before the pattern existed; the folder is on profile-a and the patterns
    // name the cache folder.
    private async Task<RepoRef> CacheAsync()
    {
        var repo = await _ws.InitializedFreshAsync();
        _ws.Write(repo, _cache, "profile a's cache");
        _ws.Write(repo, "Data/keep.txt", "kept");
        await _ws.CaptureAsync(repo, "profile-a", "A");
        await _ws.Engine.CreateBranchAsync(repo, "profile-b", "profile-a", Ct);
        await _ws.Engine.CheckoutBranchAsync(repo, "profile-b", ct: Ct);
        _ws.Write(repo, _cache, "profile b's cache, a different length");
        _ws.Write(repo, "Data/extra.pak", "an addition");
        await _ws.CaptureAsync(repo, "profile-b", "B");
        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);
        return repo with { IgnorePatterns = ["Cache/"] };
    }

    private static bool IsRefresh(GitInvocation call) => call.Arguments[0] == "update-index" && call.Arguments.Contains("--refresh");

    // The tag git prints for each index entry: S for skip-worktree, H for a normal one.
    private string TagOf(RepoRef repo, string path)
    {
        var listed = Task.Run(() => _ws.GitAsync(repo, "ls-files", "-t", "-z", "--", path), Ct).GetAwaiter().GetResult();
        return listed.SplitStandardOutputOnNul().Single().Split(' ', 2)[0];
    }

    private async Task SwitchAsync(RepoRef repo, string branch, bool force)
    {
        if (force)
        {
            await _ws.Engine.ForceCheckoutBranchAsync(repo, branch, Ct);
        }
        else
        {
            await _ws.Engine.CheckoutBranchAsync(repo, branch, ct: Ct);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_cache_is_marked_skip_worktree_while_the_index_is_refreshed_and_while_the_verify_pass_looks(bool force)
    {
        var repo = await CacheAsync();
        List<string> seen = [];
        _ws.OnInvoke = call =>
        {
            if (IsRefresh(call) || EngineWorkspace.IsVerifyDiff(call))
            {
                seen.Add($"{call.Arguments[0]} sees {TagOf(repo, _cache)}");
            }
        };

        await SwitchAsync(repo, "profile-b", force);

        Assert.Equal(["update-index sees S", "diff sees S"], seen);
        Assert.Equal("profile a's cache", File.ReadAllText(_ws.FileOf(repo, _cache)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_mark_is_gone_when_the_switch_ends_and_the_folder_reads_as_clean(bool force)
    {
        var repo = await CacheAsync();

        await SwitchAsync(repo, "profile-b", force);

        Assert.Equal("H", TagOf(repo, _cache));
        Assert.True(File.Exists(_ws.FileOf(repo, "Data/extra.pak")));
        var status = await _ws.Engine.GetStatusAsync(repo, Ct);
        Assert.True(status.IsClean, "changed: " + string.Join(", ", status.ChangedPaths) + "; new: " + string.Join(", ", status.UntrackedPaths));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_mark_is_taken_off_when_the_verify_pass_gives_up(bool force)
    {
        var repo = await CacheAsync();
        _ws.OnInvoke = call =>
        {
            if (EngineWorkspace.IsVerifyDiff(call))
            {
                File.WriteAllText(_ws.FileOf(repo, "Data/extra.pak"), "garbage from a failed write");
            }
        };

        await Assert.ThrowsAsync<GitOperationException>(() => SwitchAsync(repo, "profile-b", force));

        Assert.Equal("H", TagOf(repo, _cache));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_file_the_patterns_do_not_name_is_still_checked_and_written_again(bool force)
    {
        var repo = await CacheAsync();
        var corrupted = false;
        _ws.OnInvoke = call =>
        {
            if (EngineWorkspace.IsVerifyDiff(call) && !corrupted)
            {
                corrupted = true;
                File.WriteAllText(_ws.FileOf(repo, "Data/extra.pak"), "garbage from a failed write");
            }
        };

        await SwitchAsync(repo, "profile-b", force);

        Assert.True(corrupted);
        Assert.Equal("an addition", File.ReadAllText(_ws.FileOf(repo, "Data/extra.pak")));
        Assert.Equal("profile a's cache", File.ReadAllText(_ws.FileOf(repo, _cache)));
    }

    // The one read a left-out file gets is the alignment's: it finds out whether the bytes on disk are the target's, and if they are, the
    // entry keeps the file's stamp so that no later comparison has to read the file again.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_left_out_file_that_already_holds_the_targets_bytes_keeps_a_valid_stamp_in_the_index(bool force)
    {
        var repo = await CacheAsync();
        await _ws.Engine.CheckoutBranchAsync(repo, "profile-b", ct: Ct);

        await SwitchAsync(repo, "profile-a", force);

        var debug = (await _ws.GitAsync(repo, "ls-files", "--debug", "--", _cache)).StandardOutput;
        Assert.DoesNotContain("size: 0", debug);
    }
}
