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

    // profile-a and profile-b record the left-out file alike; the only change between them is a file the patterns do not name.
    private async Task<RepoRef> AlikeOnlyAsync()
    {
        var repo = await _ws.InitializedFreshAsync();
        _ws.Write(repo, _static, "recorded the same in both");
        await _ws.CaptureAsync(repo, "profile-a", "A");
        await _ws.Engine.CreateBranchAsync(repo, "profile-b", "profile-a", Ct);
        await _ws.Engine.CheckoutBranchAsync(repo, "profile-b", ct: Ct);
        _ws.Write(repo, "Data/extra.pak", "an addition");
        await _ws.CaptureAsync(repo, "profile-b", "B");
        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);
        return repo with { IgnorePatterns = ["Cache/"] };
    }

    [Fact]
    public async Task A_forced_switch_that_changes_no_left_out_file_keeps_the_players_copy_of_an_alike_one()
    {
        var repo = await AlikeOnlyAsync();
        _ws.Write(repo, _static, "the player's own bytes");

        await _ws.Engine.ForceCheckoutBranchAsync(repo, "profile-b", Ct);

        Assert.Equal("the player's own bytes", File.ReadAllText(_ws.FileOf(repo, _static)));
        Assert.True(File.Exists(_ws.FileOf(repo, "Data/extra.pak")));
        Assert.Empty(await MarkedAsync(repo));
    }

    [Fact]
    public async Task A_failure_to_mark_the_alike_file_leaves_no_note_of_an_unfinished_switch()
    {
        var repo = await AlikeOnlyAsync();
        _ws.OnInvoke = call =>
        {
            if (call.Arguments.Contains("--skip-worktree"))
            {
                throw new GitOperationException("cannot mark");
            }
        };

        await Assert.ThrowsAsync<GitOperationException>(() => _ws.Engine.ForceCheckoutBranchAsync(repo, "profile-b", Ct));

        Assert.False(File.Exists(SwitchJournal.PathIn(repo.GitDir)), "the note of an unfinished switch was left");
        _ws.OnInvoke = null;
        Assert.Null((await _ws.Engine.GetStatusAsync(repo, Ct)).InterruptedSwitch);
    }

    // An unfinished switch to profile-d aligned the index to profile-d, which holds Cache/p.txt; neither the point the folder is on nor
    // profile-c holds it, and marking that entry makes git refuse the merge.
    [Fact]
    public async Task A_forced_switch_to_another_target_after_an_unfinished_one_ignores_entries_the_target_lacks()
    {
        var repo = await CacheAsync();
        var plain = repo with { IgnorePatterns = [] };
        await _ws.Engine.CreateBranchAsync(plain, "profile-c", "profile-a", Ct);
        await _ws.Engine.CreateBranchAsync(plain, "profile-d", "profile-a", Ct);
        await _ws.Engine.CheckoutBranchAsync(plain, "profile-d", ct: Ct);
        _ws.Write(repo, "Cache/p.txt", "only profile d holds this");
        await _ws.CaptureAsync(plain, "profile-d", "D", ignore: []);
        await _ws.Engine.CheckoutBranchAsync(plain, "profile-c", ct: Ct);
        _ws.Write(repo, "Data/c.txt", "profile c");
        await _ws.CaptureAsync(plain, "profile-c", "C", ignore: []);
        await _ws.Engine.CheckoutBranchAsync(plain, "profile-a", ct: Ct);
        _ws.OnInvoke = call =>
        {
            if (call.Arguments[0] == "checkout" && call.Arguments.Contains("profile-d"))
            {
                throw new InvalidOperationException("killed");
            }
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => SwitchAsync(repo, "profile-d", force: false));
        _ws.OnInvoke = null;
        _ws.Write(repo, "Cache/p.txt", "the player's copy");

        await _ws.Engine.ForceCheckoutBranchAsync(repo, "profile-c", Ct);

        Assert.Equal("refs/heads/profile-c", await _ws.HeadRefAsync(repo));
        Assert.Empty(await MarkedAsync(repo));
    }

    // An index file of version 2 cannot hold a flag, so a write that finds one has no mark to look for. Other versions can, and are asked.
    [Fact]
    public async Task A_version_2_index_is_not_listed_for_marks_and_a_version_3_one_is_cleared()
    {
        var repo = await CacheAsync();
        await _ws.GitWriteAsync(repo, "update-index", "--index-version", "2");
        var before = _ws.Invocations.Count;
        _ws.Write(repo, "Data/more.txt", "more");

        await _ws.CaptureAsync(repo with { IgnorePatterns = [] }, "profile-a", "A2", ignore: []);

        Assert.DoesNotContain(_ws.Invocations.Skip(before), IsMarkListing);
        await MarkAsync(repo, _cache);
        await _ws.GitWriteAsync(repo, "update-index", "--index-version", "3");
        Assert.NotEmpty(await MarkedAsync(repo));

        await _ws.CaptureAsync(repo with { IgnorePatterns = [] }, "profile-a", "A3", ignore: []);

        Assert.Empty(await MarkedAsync(repo));
    }

    // A switch that marks nothing has no mark to look for.
    [Fact]
    public async Task A_switch_without_left_out_files_asks_git_for_marks_only_at_the_start()
    {
        var repo = await _ws.InitializedFreshAsync();
        _ws.Write(repo, "a.txt", "a");
        await _ws.CaptureAsync(repo, "profile-a", "A");
        await _ws.Engine.CreateBranchAsync(repo, "profile-b", "profile-a", Ct);
        await _ws.Engine.CheckoutBranchAsync(repo, "profile-b", ct: Ct);
        _ws.Write(repo, "b.txt", "b");
        await _ws.CaptureAsync(repo, "profile-b", "B");
        var before = _ws.Invocations.Count;

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);

        var calls = _ws.Invocations.Skip(before).ToList();
        Assert.Equal(1, calls.Count(IsMarkListing));
        Assert.True(calls.FindIndex(IsMarkListing) < calls.FindIndex(call => call.Arguments[0] == "checkout"));
    }

    [Fact]
    public async Task A_switch_that_marks_files_asks_git_once_for_marks_after_it_verified()
    {
        var repo = await CacheAsync();
        var before = _ws.Invocations.Count;
        await _ws.GitWriteAsync(repo, "update-index", "--index-version", "2");

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-b", ct: Ct);

        var calls = _ws.Invocations.Skip(before).ToList();
        Assert.Equal(1, calls.Count(IsMarkListing));
        Assert.True(calls.FindLastIndex(IsMarkListing) > calls.FindLastIndex(EngineWorkspace.IsVerifyDiff));
    }

    private static bool IsMarkListing(GitInvocation call) => call.Arguments[0] == "ls-files" && call.Arguments.Contains("-t");

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
