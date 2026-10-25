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
    private const string _static = "Cache/static.bin";

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
        _ws.Write(repo, _static, "recorded the same in both");
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

    // The tag git prints for an index entry: S for skip-worktree, H for a normal one. A copy of the index can be asked instead of the real one.
    private async Task<string> TagOfAsync(RepoRef repo, string path, string? indexFile = null)
    {
        var invocation = new GitInvocation(["ls-files", "-t", "-z", "--", path])
        {
            GitDir = repo.GitDir,
            WorkTree = repo.WorkTree,
            ReadOnly = true,
            ExtraEnvironment = indexFile is null ? null : new Dictionary<string, string?> { ["GIT_INDEX_FILE"] = indexFile },
        };
        var listed = await _ws.Runner.RunAsync(invocation, Ct);
        return listed.SplitStandardOutputOnNul().Single().Split(' ', 2)[0];
    }

    // Every entry of the index that carries the skip-worktree bit.
    private async Task<string[]> MarkedAsync(RepoRef repo) =>
        [.. (await _ws.GitAsync(repo, "ls-files", "-t", "-z")).SplitStandardOutputOnNul().Where(entry => entry.StartsWith("S ", StringComparison.Ordinal))];

    private Task MarkAsync(RepoRef repo, string path) => _ws.GitWriteAsync(repo, "update-index", "--skip-worktree", "--", path);

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
        List<(string Call, string Index)> seen = [];
        _ws.OnInvoke = call =>
        {
            if (IsRefresh(call) || EngineWorkspace.IsVerifyDiff(call))
            {
                var copy = Path.Combine(_ws.Root, $"index-{seen.Count}");
                File.Copy(Path.Combine(repo.GitDir, "index"), copy);
                seen.Add((call.Arguments[0], copy));
            }
        };

        await SwitchAsync(repo, "profile-b", force);

        List<string> tags = [];
        foreach (var (call, index) in seen)
        {
            tags.Add($"{call} sees {await TagOfAsync(repo, _cache, index)}");
        }

        Assert.Equal(["update-index sees S", "diff sees S"], tags);
        Assert.Equal("profile a's cache", File.ReadAllText(_ws.FileOf(repo, _cache)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_mark_is_gone_when_the_switch_ends_and_the_folder_reads_as_clean(bool force)
    {
        var repo = await CacheAsync();

        await SwitchAsync(repo, "profile-b", force);

        Assert.Empty(await MarkedAsync(repo));
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

        Assert.Empty(await MarkedAsync(repo));
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

    // A switch killed after it marked the entries never runs its clean-up. The next discard (and every other write) must not keep the mark:
    // it would hide the file from the saves made after the patterns are dropped.
    [Fact]
    public async Task A_mark_left_by_a_killed_switch_is_gone_after_a_discard_and_the_next_save_records_the_file()
    {
        var repo = await CacheAsync();
        _ws.OnInvoke = call =>
        {
            if (call.Arguments[0] == "checkout" && call.Arguments.Contains("profile-b"))
            {
                throw new InvalidOperationException("killed");
            }

            if (call.Arguments.Contains("--no-skip-worktree"))
            {
                throw new IOException("killed");
            }
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => SwitchAsync(repo, "profile-b", force: false));
        Assert.NotEmpty(await MarkedAsync(repo));
        _ws.OnInvoke = null;

        await _ws.Engine.DiscardChangesAsync(repo, Ct);

        Assert.Empty(await MarkedAsync(repo));
        _ws.Write(repo, _cache, "edited after the patterns were dropped");
        await _ws.CaptureAsync(repo with { IgnorePatterns = [] }, "profile-a", "A2", ignore: []);
        Assert.Equal("edited after the patterns were dropped", await _ws.GitOutAsync(repo, "show", "profile-a:" + _cache));
    }

    [Fact]
    public async Task A_save_clears_a_leftover_mark_before_it_records_the_folder()
    {
        var repo = await CacheAsync();
        await MarkAsync(repo, _cache);
        _ws.Write(repo, _cache, "edited after the patterns were dropped");

        await _ws.CaptureAsync(repo with { IgnorePatterns = [] }, "profile-a", "A2", ignore: []);

        Assert.Equal("edited after the patterns were dropped", await _ws.GitOutAsync(repo, "show", "profile-a:" + _cache));
        Assert.Empty(await MarkedAsync(repo));
    }

    [Fact]
    public async Task A_switch_clears_a_leftover_mark_before_it_looks_for_unsaved_changes()
    {
        var repo = await CacheAsync();
        await MarkAsync(repo, "Data/keep.txt");
        _ws.Write(repo, "Data/keep.txt", "an edit the mark would hide");

        await Assert.ThrowsAsync<DirtyWorkingTreeException>(() => SwitchAsync(repo, "profile-b", force: false));

        Assert.Empty(await MarkedAsync(repo));
    }

    // Both snapshots record the file with the same bytes, so the switch has no change to report for it; the player's copy still stays.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_left_out_file_both_snapshots_record_alike_keeps_the_players_bytes_through_a_switch(bool force)
    {
        var repo = await CacheAsync();
        _ws.Write(repo, _static, "the player's own bytes");

        await SwitchAsync(repo, "profile-b", force);

        Assert.Equal("the player's own bytes", File.ReadAllText(_ws.FileOf(repo, _static)));
        Assert.Empty(await MarkedAsync(repo));
    }

    // A stop or a failure ends the switch from inside the verify pass; the mark must not outlive it, and what it reports is the real error.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_stop_during_the_refresh_leaves_no_mark_and_a_failing_clean_up_does_not_replace_the_stop(bool clearFails)
    {
        var repo = await CacheAsync();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        _ws.OnInvoke = call =>
        {
            if (IsRefresh(call))
            {
                stop.Cancel();
            }

            if (clearFails && call.Arguments.Contains("--no-skip-worktree"))
            {
                throw new DirectoryNotFoundException("the git folder went away");
            }
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _ws.Engine.CheckoutBranchAsync(repo, "profile-b", ct: stop.Token));

        if (!clearFails)
        {
            Assert.Empty(await MarkedAsync(repo));
        }
    }

    [Fact]
    public async Task A_switch_refused_when_git_finds_a_file_in_the_way_ends_with_no_mark()
    {
        var repo = await CacheAsync();
        _ws.OnInvoke = call =>
        {
            if (call.Arguments[0] == "checkout" && call.Arguments.Contains("profile-b"))
            {
                File.WriteAllText(_ws.FileOf(repo, "Data/extra.pak"), "the player's own file");
            }
        };

        await Assert.ThrowsAsync<DirtyWorkingTreeException>(() => SwitchAsync(repo, "profile-b", force: false));

        Assert.Empty(await MarkedAsync(repo));
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
