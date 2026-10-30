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

    // Each world below is built once per test run and copied for every test that starts from it; the patterns are not part of a copy, so
    // they are put on after it.
    private async Task<RepoRef> CopiedAsync(string name, Func<EngineWorkspace, Task<RepoRef>> build, bool leftOut = true)
    {
        var (repo, _) = await RepoTemplates.CopyAsync<string?>("left-out-read-once-" + name, _ws, async ws => (await build(ws), null));
        return leftOut ? repo with { IgnorePatterns = ["Cache/"] } : repo;
    }

    // Both profiles record the cache, with different bytes, from before the pattern existed; the folder is on profile-a and the patterns
    // name the cache folder.
    private Task<RepoRef> CacheAsync() => CopiedAsync("cache", async ws =>
    {
        var repo = await ws.InitializedFreshAsync();
        ws.Write(repo, _cache, "profile a's cache");
        ws.Write(repo, "Data/keep.txt", "kept");
        ws.Write(repo, _static, "recorded the same in both");
        await ws.CaptureAsync(repo, "profile-a", "A");
        await ws.Engine.CreateBranchAsync(repo, "profile-b", "profile-a", Ct);
        await ws.Engine.CheckoutBranchAsync(repo, "profile-b", ct: Ct);
        ws.Write(repo, _cache, "profile b's cache, a different length");
        ws.Write(repo, "Data/extra.pak", "an addition");
        await ws.CaptureAsync(repo, "profile-b", "B");
        await ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);
        return repo;
    });

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
    private Task<RepoRef> AlikeOnlyAsync() => CopiedAsync("alike-only", async ws =>
    {
        var repo = await ws.InitializedFreshAsync();
        ws.Write(repo, _static, "recorded the same in both");
        await ws.CaptureAsync(repo, "profile-a", "A");
        await ws.Engine.CreateBranchAsync(repo, "profile-b", "profile-a", Ct);
        await ws.Engine.CheckoutBranchAsync(repo, "profile-b", ct: Ct);
        ws.Write(repo, "Data/extra.pak", "an addition");
        await ws.CaptureAsync(repo, "profile-b", "B");
        await ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);
        return repo;
    });

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

    // A repository is created with index version 4, so the shortcut has to read the entries, not the version, to know there is no mark.
    [Fact]
    public async Task A_save_into_a_version_4_index_without_marks_does_not_list_them()
    {
        var repo = await CacheAsync();
        Assert.Equal("4", (await _ws.GitAsync(repo, "config", "index.version")).StandardOutput.Trim());
        var before = _ws.Invocations.Count;
        _ws.Write(repo, "Data/more.txt", "more");

        await _ws.CaptureAsync(repo with { IgnorePatterns = [] }, "profile-a", "A2", ignore: []);

        Assert.DoesNotContain(_ws.Invocations.Skip(before), IsMarkListing);
    }

    [Fact]
    public async Task A_save_into_a_version_4_index_with_a_mark_lists_and_clears_it()
    {
        var repo = await CacheAsync();
        await MarkAsync(repo, _cache);
        Assert.NotEmpty(await MarkedAsync(repo));
        var before = _ws.Invocations.Count;

        await _ws.CaptureAsync(repo with { IgnorePatterns = [] }, "profile-a", "A2", ignore: []);

        Assert.Contains(_ws.Invocations.Skip(before), IsMarkListing);
        Assert.Empty(await MarkedAsync(repo));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task An_older_index_version_is_read_the_same_way(int version)
    {
        var repo = await CacheAsync();
        await _ws.GitWriteAsync(repo, "update-index", "--index-version", version.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var before = _ws.Invocations.Count;
        _ws.Write(repo, "Data/more.txt", "more");
        await _ws.CaptureAsync(repo with { IgnorePatterns = [] }, "profile-a", "A2", ignore: []);
        Assert.DoesNotContain(_ws.Invocations.Skip(before), IsMarkListing);

        await MarkAsync(repo, _cache);
        await _ws.GitWriteAsync(repo, "update-index", "--index-version", version.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await _ws.CaptureAsync(repo with { IgnorePatterns = [] }, "profile-a", "A3", ignore: []);

        Assert.Empty(await MarkedAsync(repo));
    }

    // An index the reader cannot make sense of is handed to git, which gives the real answer (or the real error).
    [Fact]
    public async Task An_index_that_cannot_be_read_is_listed_by_git()
    {
        var repo = await CacheAsync();
        var path = Path.Combine(repo.GitDir, "index");
        var good = File.ReadAllBytes(path);
        File.WriteAllBytes(path, good[..(good.Length / 2)]);
        var before = _ws.Invocations.Count;

        // The save goes first: a diff over a cut-short index (status runs one) can write a whole index back where the files' stamps changed,
        // which happens in a copied folder, and the reader then has nothing left that it cannot read.
        await Record.ExceptionAsync(() => _ws.CaptureAsync(repo with { IgnorePatterns = [] }, "profile-a", "A2", ignore: []));
        await Record.ExceptionAsync(() => _ws.Engine.GetStatusAsync(repo, Ct));

        Assert.Contains(_ws.Invocations.Skip(before), IsMarkListing);
    }

    // A switch that marks nothing has no mark to look for.
    [Fact]
    public async Task A_switch_without_left_out_files_never_asks_git_for_marks()
    {
        var repo = await CopiedAsync("no-left-out-files", async ws =>
        {
            var made = await ws.InitializedFreshAsync();
            ws.Write(made, "a.txt", "a");
            await ws.CaptureAsync(made, "profile-a", "A");
            await ws.Engine.CreateBranchAsync(made, "profile-b", "profile-a", Ct);
            await ws.Engine.CheckoutBranchAsync(made, "profile-b", ct: Ct);
            ws.Write(made, "b.txt", "b");
            await ws.CaptureAsync(made, "profile-b", "B");
            return made;
        }, leftOut: false);
        var before = _ws.Invocations.Count;

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);

        var calls = _ws.Invocations.Skip(before).ToList();
        Assert.DoesNotContain(calls, IsMarkListing);
    }

    [Fact]
    public async Task A_switch_that_marks_files_asks_git_once_for_marks_after_it_verified()
    {
        var repo = await CacheAsync();
        var before = _ws.Invocations.Count;

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-b", ct: Ct);

        var calls = _ws.Invocations.Skip(before).ToList();
        Assert.Equal(1, calls.Count(IsMarkListing));
        Assert.True(calls.FindLastIndex(IsMarkListing) > calls.FindLastIndex(EngineWorkspace.IsVerifyDiff));
    }

    // A save's listing also carries tags, but it asks for new files too; the marks are asked for alone.
    private static bool IsMarkListing(GitInvocation call) => call.Arguments[0] == "ls-files" && call.Arguments.Contains("-t") && !call.Arguments.Contains("-o");

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

    // The id and mode the index holds for a path ("100644 <id>"); empty when it has no entry.
    private async Task<string> IndexEntryAsync(RepoRef repo, string path)
    {
        var line = (await _ws.GitAsync(repo, "ls-files", "-s", "-z", "--", path)).SplitStandardOutputOnNul().SingleOrDefault();
        return line is null ? string.Empty : string.Join(' ', line.Split('\t')[0].Split(' ').Take(2));
    }

    private async Task<string> TargetEntryAsync(RepoRef repo, string branch, string path)
    {
        var line = (await _ws.GitAsync(repo, "ls-tree", "-z", branch, "--", path)).SplitStandardOutputOnNul().Single();
        var fields = line.Split('\t')[0].Split(' ');
        return $"{fields[0]} {fields[2]}";
    }

    // A file whose size is not the target's blob's cannot hold the target's bytes, so there is nothing for git to find out by reading it:
    // the entry is written from the switch plan, which is what keeps a big cache from being read on every switch.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_left_out_file_of_another_size_than_the_target_is_aligned_without_a_reset(bool force)
    {
        var repo = await CacheAsync();
        var before = _ws.Invocations.Count;

        await SwitchAsync(repo, "profile-b", force);

        Assert.DoesNotContain(_ws.Invocations.Skip(before), call => call.Arguments[0] == "reset");
        Assert.Equal(await TargetEntryAsync(repo, "profile-b", _cache), await IndexEntryAsync(repo, _cache));
        Assert.Equal("profile a's cache", File.ReadAllText(_ws.FileOf(repo, _cache)));
    }

    // The size alone cannot tell the bytes apart; such a file is read, and the entry still says what the target says.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_left_out_file_the_size_of_the_target_with_other_bytes_is_aligned_to_the_targets_entry_and_kept(bool force)
    {
        var repo = await CacheAsync();
        var lookalike = new string('x', "profile b's cache, a different length".Length);
        _ws.Write(repo, _cache, lookalike);

        await SwitchAsync(repo, "profile-b", force);

        Assert.Equal(await TargetEntryAsync(repo, "profile-b", _cache), await IndexEntryAsync(repo, _cache));
        Assert.Equal(lookalike, File.ReadAllText(_ws.FileOf(repo, _cache)));
        Assert.Empty(await MarkedAsync(repo));
        var status = await _ws.Engine.GetStatusAsync(repo, Ct);
        Assert.True(status.IsClean, "changed: " + string.Join(", ", status.ChangedPaths) + "; new: " + string.Join(", ", status.UntrackedPaths));
    }

    // Names with spaces and non-ASCII letters reach git whole.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_left_out_file_with_spaces_and_accents_in_its_name_is_aligned(bool force)
    {
        const string name = "Cache/mod pack/café [draft] 1.bin";
        var repo = await CopiedAsync("accents", async ws =>
        {
            var made = await ws.InitializedFreshAsync();
            ws.Write(made, name, "profile a's bytes");
            await ws.CaptureAsync(made, "profile-a", "A");
            await ws.Engine.CreateBranchAsync(made, "profile-b", "profile-a", Ct);
            await ws.Engine.CheckoutBranchAsync(made, "profile-b", ct: Ct);
            ws.Write(made, name, "profile b's bytes, longer");
            await ws.CaptureAsync(made, "profile-b", "B");
            await ws.Engine.CheckoutBranchAsync(made, "profile-a", ct: Ct);
            return made;
        });

        await SwitchAsync(repo, "profile-b", force);

        Assert.Equal(await TargetEntryAsync(repo, "profile-b", name), await IndexEntryAsync(repo, name));
        Assert.Equal("profile a's bytes", File.ReadAllText(_ws.FileOf(repo, name)));
    }

    // A file the target lacks leaves the index; its file stays where the player has it.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_left_out_file_the_target_lacks_leaves_the_index_and_stays_on_disk(bool force)
    {
        var repo = await CopiedAsync("target-lacks", async ws =>
        {
            var made = await ws.InitializedFreshAsync();
            ws.Write(made, "Cache/only-a.bin", "only in a");
            ws.Write(made, "Cache/both.bin", "in both, as a has it");
            await ws.CaptureAsync(made, "profile-a", "A");
            await ws.Engine.CreateBranchAsync(made, "profile-b", "profile-a", Ct);
            await ws.Engine.CheckoutBranchAsync(made, "profile-b", ct: Ct);
            ws.Delete(made, "Cache/only-a.bin");
            ws.Write(made, "Cache/both.bin", "in both, as b has it");
            await ws.CaptureAsync(made, "profile-b", "B");
            await ws.Engine.CheckoutBranchAsync(made, "profile-a", ct: Ct);
            return made;
        });

        await SwitchAsync(repo, "profile-b", force);

        Assert.Equal(string.Empty, await IndexEntryAsync(repo, "Cache/only-a.bin"));
        Assert.Equal(await TargetEntryAsync(repo, "profile-b", "Cache/both.bin"), await IndexEntryAsync(repo, "Cache/both.bin"));
        Assert.Equal("only in a", File.ReadAllText(_ws.FileOf(repo, "Cache/only-a.bin")));
        Assert.Empty(await MarkedAsync(repo));
    }

    // A name that differs only in case is one file on Windows and macOS: after the switch one entry remains, with the target's id.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_left_out_file_renamed_only_in_case_ends_with_one_entry_that_has_the_targets_id(bool force)
    {
        var repo = await CopiedAsync("case-rename", async ws =>
        {
            var made = await ws.InitializedFreshAsync();
            ws.Write(made, "Cache/Big.bin", "profile a's cache");
            await ws.CaptureAsync(made, "profile-a", "A");
            await ws.Engine.CreateBranchAsync(made, "profile-b", "profile-a", Ct);
            await ws.Engine.CheckoutBranchAsync(made, "profile-b", ct: Ct);
            ws.Delete(made, "Cache/Big.bin");
            ws.Write(made, "Cache/big.bin", "profile b's cache, a different length");

            // A drive that ignores case keeps the old spelling in the index, so the rename is made there on purpose.
            await ws.GitWriteAsync(made, "rm", "--cached", "-q", "--", "Cache/Big.bin");
            await ws.CaptureAsync(made, "profile-b", "B");
            Assert.Equal("Cache/big.bin", (await ws.GitAsync(made, "ls-tree", "-r", "--name-only", "profile-b")).StandardOutput.Trim());
            await ws.Engine.CheckoutBranchAsync(made, "profile-a", ct: Ct);
            return made;
        });

        await SwitchAsync(repo, "profile-b", force);

        var listed = (await _ws.GitAsync(repo, "ls-files", "-s", "-z", "--", "Cache")).SplitStandardOutputOnNul();
        var entry = Assert.Single(listed);
        var target = await TargetEntryAsync(repo, "profile-b", "Cache/big.bin");
        Assert.StartsWith(target, entry, StringComparison.Ordinal);
    }

    // profile-a holds three left-out files: one the same size as profile-b's (it might already hold b's bytes), one of another size, and
    // one profile-b lacks. The folder is on profile-a.
    private Task<RepoRef> MixedAsync() => CopiedAsync("mixed", async ws =>
    {
        var repo = await ws.InitializedFreshAsync();
        ws.Write(repo, "Cache/same-size.bin", "one");
        ws.Write(repo, "Cache/other-size.bin", "short");
        ws.Write(repo, "Cache/only-a.bin", "only in a");
        await ws.CaptureAsync(repo, "profile-a", "A");
        await ws.Engine.CreateBranchAsync(repo, "profile-b", "profile-a", Ct);
        await ws.Engine.CheckoutBranchAsync(repo, "profile-b", ct: Ct);
        ws.Write(repo, "Cache/same-size.bin", "two");
        ws.Write(repo, "Cache/other-size.bin", "a much longer text");
        ws.Delete(repo, "Cache/only-a.bin");
        await ws.CaptureAsync(repo, "profile-b", "B");
        await ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);
        return repo;
    });

    private async Task<string> FullIndexAsync(RepoRef repo) =>
        string.Join('\n', (await _ws.GitAsync(repo, "ls-files", "-s", "-t", "-z")).SplitStandardOutputOnNul());

    // Removals go in first, the reset sees only the path that might hold the target's bytes, and the entries written from the plan follow.
    [Fact]
    public async Task A_switch_with_a_same_size_file_and_a_different_size_file_resets_only_the_first_and_writes_the_rest_around_it()
    {
        var repo = await MixedAsync();
        var before = _ws.Invocations.Count;

        await SwitchAsync(repo, "profile-b", force: false);

        // The capture of an earlier snapshot also writes entries with --add; only the switch's own calls are looked at.
        var calls = _ws.Invocations.Skip(before).Where(EngineWorkspace.IsIndexAlignment).Where(call => !call.Arguments.Contains("--add")).ToList();
        Assert.Equal(3, calls.Count);
        Assert.Equal("update-index", calls[0].Arguments[0]);
        Assert.StartsWith("0 ", calls[0].Stdin, StringComparison.Ordinal);
        Assert.Contains("Cache/only-a.bin", calls[0].Stdin, StringComparison.Ordinal);
        Assert.DoesNotContain("other-size", calls[0].Stdin, StringComparison.Ordinal);
        Assert.Equal("reset", calls[1].Arguments[0]);
        Assert.Equal("Cache/same-size.bin\0", calls[1].Stdin);
        Assert.Equal("update-index", calls[2].Arguments[0]);
        Assert.Contains("Cache/other-size.bin", calls[2].Stdin, StringComparison.Ordinal);
        Assert.DoesNotContain("same-size", calls[2].Stdin, StringComparison.Ordinal);
    }

    // The reset worked and the entries from the plan then fail to go in: the index is put back as it was, with no note.
    [Fact]
    public async Task A_failure_writing_the_entries_after_the_reset_puts_the_original_index_back()
    {
        var repo = await MixedAsync();
        var before = await FullIndexAsync(repo);
        var reset = false;
        _ws.OnInvoke = call =>
        {
            if (call.Arguments[0] == "reset" && EngineWorkspace.IsIndexAlignment(call))
            {
                reset = true;
            }
            else if (reset && EngineWorkspace.IsIndexAlignment(call))
            {
                throw new GitOperationException("the index lock stayed held");
            }
        };

        await Assert.ThrowsAsync<GitOperationException>(() => SwitchAsync(repo, "profile-b", force: false));

        _ws.OnInvoke = null;
        Assert.True(reset);
        Assert.Equal(before, await FullIndexAsync(repo));
        Assert.Null(SwitchJournal.Read(repo.GitDir));
        Assert.Empty(Directory.EnumerateFiles(repo.GitDir, "savepoint-align-*"));
        Assert.Equal("refs/heads/profile-a", await _ws.HeadRefAsync(repo));
    }

    // Windows cannot hold some names a snapshot can record (a colon in a name). git leaves such a name out of the named paths without
    // failing, so the switch must still be refused, with the name.
    [Fact]
    public async Task A_name_the_system_cannot_hold_refuses_the_switch_and_names_it()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows refuses names with a colon");
        var repo = await _ws.InitializedFreshAsync();
        _ws.Write(repo, "Cache/ok.bin", "fine");
        await _ws.CaptureAsync(repo, "profile-a", "A");
        await _ws.Engine.CreateBranchAsync(repo, "profile-b", "profile-a", Ct);
        var blob = (await PlumbingAsync(repo, "x", "hash-object", "-w", "--stdin")).Trim();
        var cache = (await PlumbingAsync(repo, $"100644 blob {blob}\ta:b.bin\n100644 blob {blob}\tok.bin\n", "mktree")).Trim();
        var root = (await PlumbingAsync(repo, $"040000 tree {cache}\tCache\n", "mktree")).Trim();
        var parent = (await _ws.GitOutAsync(repo, "rev-parse", "profile-a")).Trim();
        var commit = (await PlumbingAsync(repo, string.Empty, "commit-tree", root, "-p", parent, "-m", "B")).Trim();
        await _ws.GitWriteAsync(repo, "update-ref", "refs/heads/profile-b", commit);
        repo = repo with { IgnorePatterns = ["Cache/"] };

        var thrown = await Record.ExceptionAsync(() => SwitchAsync(repo, "profile-b", force: false));

        Assert.IsType<GitOperationException>(thrown);
        Assert.Contains("a:b.bin", thrown.Message, StringComparison.Ordinal);
        Assert.Equal("refs/heads/profile-a", await _ws.HeadRefAsync(repo));
    }

    private async Task<string> PlumbingAsync(RepoRef repo, string stdin, params string[] arguments) =>
        (await _ws.Runner.RunAsync(new GitInvocation(arguments) { GitDir = repo.GitDir, WorkTree = repo.WorkTree, Stdin = stdin }, Ct)).StandardOutput;
}
