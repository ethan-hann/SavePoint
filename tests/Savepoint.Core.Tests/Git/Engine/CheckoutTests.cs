using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

[Collection(EngineCollections.A)]
public sealed class CheckoutTests : IDisposable
{
    private const string _accent = "caf\u00e9/cr\u00e8me br\u00fbl\u00e9e.esp";

    private readonly EngineWorkspace _ws = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    [Fact]
    public async Task Switching_between_two_profiles_and_back_restores_every_byte()
    {
        var (repo, a, b) = await TwoProfilesAsync();

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);
        Assert.Equal(a, TreeSnapshot.Take(repo.WorkTree));
        await _ws.Engine.CheckoutBranchAsync(repo, "profile-b", ct: Ct);
        Assert.Equal(b, TreeSnapshot.Take(repo.WorkTree));
        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);
        Assert.Equal(a, TreeSnapshot.Take(repo.WorkTree));
        await _ws.Engine.CheckoutBranchAsync(repo, "profile-b", ct: Ct);
        Assert.Equal(b, TreeSnapshot.Take(repo.WorkTree));
    }

    [Fact]
    public async Task Names_with_spaces_and_non_ascii_characters_survive_the_round_trip()
    {
        var (repo, a, _) = await TwoProfilesAsync();

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);

        Assert.Contains(_accent, a.Keys);
        Assert.True(File.Exists(_ws.FileOf(repo, _accent)));
        Assert.True(File.Exists(_ws.FileOf(repo, "Data/sub dir/it's [draft] (1).txt")));
    }

    [Fact]
    public async Task A_path_longer_than_260_characters_round_trips()
    {
        var repo = await _ws.InitializedRepoAsync();
        var deep = string.Join('/', Enumerable.Repeat("a-folder-name-of-about-forty-characters-x", 8)) + "/leaf.txt";
        Assert.True(_ws.FileOf(repo, deep).Length > 300);
        _ws.Write(repo, deep, "deep content");
        _ws.Write(repo, "root.txt", "root");
        await _ws.CaptureAsync(repo, "profile-a");
        await _ws.GitWriteAsync(repo, "checkout", "--quiet", "-b", "profile-b");
        _ws.Delete(repo, deep);
        await _ws.CaptureAsync(repo, "profile-b");

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);

        Assert.Equal("deep content", await File.ReadAllTextAsync(_ws.FileOf(repo, deep), Ct));
        Assert.Contains(deep, await _ws.TreeAsync(repo, "profile-a"));
    }

    [Fact]
    public async Task A_point_can_be_checked_out_by_id_leaving_the_folder_detached()
    {
        var (repo, a, _) = await TwoProfilesAsync();
        var older = await _ws.TipAsync(repo, "profile-a");

        await _ws.Engine.CheckoutCommitAsync(repo, older!, ct: Ct);

        var status = await _ws.Engine.GetStatusAsync(repo, Ct);
        Assert.True(status.IsDetached);
        Assert.Equal(older, status.HeadCommit);
        Assert.Equal(a, TreeSnapshot.Take(repo.WorkTree));

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-b", ct: Ct);
        Assert.False((await _ws.Engine.GetStatusAsync(repo, Ct)).IsDetached);
    }

    [Theory]
    [InlineData("abc1234")]
    [InlineData("profile-a")]
    [InlineData("")]
    public async Task A_commit_checkout_needs_a_full_commit_id(string commitId)
    {
        var (repo, _, _) = await TwoProfilesAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => _ws.Engine.CheckoutCommitAsync(repo, commitId, ct: Ct));
    }

    [Fact]
    public async Task A_branch_without_points_or_a_commit_that_does_not_exist_is_refused_and_nothing_changes()
    {
        var (repo, _, b) = await TwoProfilesAsync();

        var empty = await Assert.ThrowsAsync<ProfileHasNoPointsException>(() => _ws.Engine.CheckoutBranchAsync(repo, "ghost", ct: Ct));
        Assert.Equal("ghost", empty.Args["profileId"]);
        var missing = await Assert.ThrowsAsync<GitOperationException>(() => _ws.Engine.CheckoutCommitAsync(repo, new string('1', 40), ct: Ct));
        Assert.Equal("error.gitOperation.failed", missing.MessageId);

        Assert.Equal(b, TreeSnapshot.Take(repo.WorkTree));
        Assert.Equal("refs/heads/profile-b", await _ws.HeadRefAsync(repo));
    }

    [Fact]
    public async Task Checking_out_the_empty_branch_empties_the_folder_but_keeps_the_marker()
    {
        var (repo, a, _) = await TwoProfilesAsync();
        await _ws.Engine.CreateEmptyBranchAsync(repo, "fresh", ct: Ct);

        await _ws.Engine.CheckoutBranchAsync(repo, "fresh", ct: Ct);

        Assert.Empty(TreeSnapshot.Take(repo.WorkTree));
        Assert.Equal("game-1", EngineWorkspace.RequireMarker(repo).GameId);
        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);
        Assert.Equal(a, TreeSnapshot.Take(repo.WorkTree));
        _ws.Write(repo, "new-after-fresh.txt", "x");
        Assert.True((await _ws.CaptureAsync(repo, "profile-a")).Changed);
    }

    [Fact]
    public async Task Progress_ends_complete_never_goes_backwards_and_keeps_one_total()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.WriteMany(repo, "Data", 2500, content: "a");
        await _ws.CaptureAsync(repo, "profile-a");
        await _ws.GitWriteAsync(repo, "checkout", "--quiet", "-b", "profile-b");
        _ws.WriteMany(repo, "Data", 2500, content: "b");
        await _ws.CaptureAsync(repo, "profile-b");
        var progress = new ProgressLog();

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", progress, Ct);

        var reports = progress.Reports;
        Assert.NotEmpty(reports);
        Assert.Equal((2500, 2500), (progress.Last.FilesDone, progress.Last.FilesTotal));

        // Every file differs between the two profiles ("a 0" against "b 0"), so the switch writes all of them.
        var written = Enumerable.Range(0, 2500).Sum(i => $"a {i}".Length);
        Assert.Equal(written, progress.Last.BytesTotal);
        Assert.Equal(written, progress.Last.BytesDone);
        Assert.Equal(reports.OrderBy(report => report.FilesDone), reports);
        Assert.Single(reports.Select(report => report.FilesTotal).Distinct());
    }

    [Fact]
    public async Task Git_is_asked_for_its_progress_meter_only_when_someone_listens()
    {
        var (repo, _, _) = await TwoProfilesAsync();

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);
        await _ws.Engine.CheckoutBranchAsync(repo, "profile-b", new ProgressLog(), Ct);

        var checkouts = _ws.Invocations.Where(invocation => invocation.Arguments[0] == "checkout" && !invocation.Arguments.Contains("--quiet")).ToArray();
        Assert.Equal(2, checkouts.Length);
        Assert.DoesNotContain("--progress", checkouts[0].Arguments);
        Assert.Contains("--progress", checkouts[1].Arguments);
    }

    [Fact]
    public async Task A_cancelled_token_before_the_switch_throws_and_changes_nothing()
    {
        var (repo, _, b) = await TwoProfilesAsync();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: cancelled.Token));

        Assert.Equal(b, TreeSnapshot.Take(repo.WorkTree));
        Assert.Null((await _ws.Engine.GetStatusAsync(repo, Ct)).InterruptedSwitch);
    }

    [Fact]
    public async Task A_stale_index_lock_is_cleared_before_a_switch()
    {
        var (repo, a, _) = await TwoProfilesAsync();
        var lockFile = Path.Combine(repo.GitDir, "index.lock");
        await File.WriteAllTextAsync(lockFile, string.Empty, Ct);

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);

        Assert.Equal(a, TreeSnapshot.Take(repo.WorkTree));
        Assert.False(File.Exists(lockFile));
    }

    [Fact]
    public async Task A_lock_another_process_holds_for_a_moment_is_retried()
    {
        var ws = new EngineWorkspace(options => options with { LockRetryDelays = [TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50)] });
        using var _ = ws;
        var (repo, a, _) = await TwoProfilesAsync(ws);
        var lockFile = Path.Combine(repo.GitDir, "index.lock");
        var checkouts = 0;
        ws.OnInvoke = invocation =>
        {
            if (invocation.Arguments[0] == "checkout" && invocation.Arguments.Contains("profile-a"))
            {
                if (checkouts++ == 0)
                {
                    File.WriteAllText(lockFile, string.Empty);
                }
                else
                {
                    File.Delete(lockFile);
                }
            }
        };

        await ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);

        Assert.Equal(2, checkouts);
        Assert.Equal(a, TreeSnapshot.Take(repo.WorkTree));
    }

    [Fact]
    public async Task A_lock_that_never_goes_away_is_reported_after_the_retries()
    {
        var ws = new EngineWorkspace(options => options with { LockRetryDelays = [TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(20)] });
        using var _ = ws;
        var (repo, _, b) = await TwoProfilesAsync(ws);
        var lockFile = Path.Combine(repo.GitDir, "index.lock");
        var checkouts = 0;
        ws.OnInvoke = invocation =>
        {
            if (invocation.Arguments[0] == "checkout" && invocation.Arguments.Contains("profile-a"))
            {
                checkouts++;
                File.WriteAllText(lockFile, string.Empty);
            }
        };

        var error = await Assert.ThrowsAsync<GitOperationException>(() => ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct));

        Assert.Contains("index.lock", error.Message, StringComparison.Ordinal);
        Assert.Equal(3, checkouts);
        Assert.Equal(b, TreeSnapshot.Take(repo.WorkTree));
    }

    [Fact]
    public async Task A_low_work_tree_volume_refuses_before_any_file_is_written()
    {
        var (repo, _, b) = await TwoProfilesAsync();
        _ws.Space.PerPath = path => IsUnder(path, _ws.GamesRoot) ? 10 : null;

        var error = await Assert.ThrowsAsync<DiskSpaceException>(() => _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct));

        Assert.Equal("error.diskSpace.low", error.MessageId);
        Assert.Equal(b, TreeSnapshot.Take(repo.WorkTree));
        Assert.Equal("refs/heads/profile-b", await _ws.HeadRefAsync(repo));
    }

    [Fact]
    public async Task The_switch_must_leave_room_for_the_files_it_adds()
    {
        const long floor = 1_000;
        using var ws = new EngineWorkspace(options => options with { MinFreeBytesForCheckout = floor });
        var repo = await ws.InitializedRepoAsync();
        ws.Write(repo, "small.txt", "x");
        await ws.CaptureAsync(repo, "profile-a");
        await ws.GitWriteAsync(repo, "checkout", "--quiet", "-b", "profile-b");
        ws.Write(repo, "big.bin", new byte[300_000]);
        await ws.CaptureAsync(repo, "profile-b");
        await ws.GitWriteAsync(repo, "checkout", "--quiet", "profile-a");
        ws.Space.PerPath = path => IsUnder(path, ws.GamesRoot) ? floor + 200_000 : null;

        var error = await Assert.ThrowsAsync<DiskSpaceException>(() => ws.Engine.CheckoutBranchAsync(repo, "profile-b", ct: Ct));

        Assert.Equal(floor + 300_000, error.Args["requiredBytes"]);
        Assert.Equal(floor + 200_000, error.Args["freeBytes"]);
        Assert.False(File.Exists(ws.FileOf(repo, "big.bin")));
    }

    [Fact]
    public async Task A_switch_that_frees_space_needs_only_the_floor()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "big.bin", new byte[300_000]);
        await _ws.CaptureAsync(repo, "profile-a");
        await _ws.GitWriteAsync(repo, "checkout", "--quiet", "-b", "profile-b");
        _ws.Delete(repo, "big.bin");
        _ws.Write(repo, "small.txt", "x");
        await _ws.CaptureAsync(repo, "profile-b");
        await _ws.GitWriteAsync(repo, "checkout", "--quiet", "profile-a");
        _ws.Space.PerPath = path => IsUnder(path, _ws.GamesRoot) ? 100L * 1024 * 1024 + 10 : null;

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-b", ct: Ct);

        Assert.False(File.Exists(_ws.FileOf(repo, "big.bin")));
        Assert.True(File.Exists(_ws.FileOf(repo, "small.txt")));
    }

    [Fact]
    public async Task Local_changes_to_a_file_the_switch_would_replace_are_refused_and_kept()
    {
        var (repo, _, b) = await TwoProfilesAsync();
        _ws.Write(repo, "readme.txt", "my own edit");

        var error = await Assert.ThrowsAsync<DirtyWorkingTreeException>(() => _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct));

        Assert.Equal("error.launch.dirtyWorkingTree", error.MessageId);
        Assert.Equal(RepoRole.Install, error.Args["role"]);
        Assert.Equal("my own edit", await File.ReadAllTextAsync(_ws.FileOf(repo, "readme.txt"), Ct));
        Assert.Equal("refs/heads/profile-b", await _ws.HeadRefAsync(repo));
        Assert.Null((await _ws.Engine.GetStatusAsync(repo, Ct)).InterruptedSwitch);
        Assert.NotEqual(b["readme.txt"], TreeSnapshot.Take(repo.WorkTree)["readme.txt"]);
    }

    [Fact]
    public async Task An_untracked_file_in_the_way_is_refused_and_kept()
    {
        var (repo, _, _) = await TwoProfilesAsync();
        _ws.Write(repo, "only-a.txt", "I made this myself");

        await Assert.ThrowsAsync<DirtyWorkingTreeException>(() => _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct));

        Assert.Equal("I made this myself", await File.ReadAllTextAsync(_ws.FileOf(repo, "only-a.txt"), Ct));
    }

    [Fact]
    public async Task A_switch_refuses_a_folder_without_this_games_marker()
    {
        var (repo, _, _) = await TwoProfilesAsync();
        File.Delete(EngineWorkspace.MarkerPath(repo));

        await Assert.ThrowsAsync<ConsentMarkerException>(() => _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct));
        await Assert.ThrowsAsync<ConsentMarkerException>(() => _ws.Engine.CheckoutBranchAsync(EngineWorkspace.Claim(repo, "other", RepoRole.External), "profile-a", ct: Ct));
    }

    [Fact]
    public async Task A_switch_refuses_a_protected_work_tree()
    {
        var (repo, _, b) = await TwoProfilesAsync();
        var protectedRepo = repo with { WorkTree = _ws.Platform.Directories.HomeDir };
        Directory.CreateDirectory(protectedRepo.WorkTree);

        await Assert.ThrowsAsync<PathSafetyException>(() => _ws.Engine.CheckoutBranchAsync(protectedRepo, "profile-a", ct: Ct));

        Assert.Equal(b, TreeSnapshot.Take(repo.WorkTree));
    }

    private static bool IsUnder(string path, string root) =>
        Path.GetFullPath(path).StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase);

    private Task<(RepoRef Repo, SortedDictionary<string, string> A, SortedDictionary<string, string> B)> TwoProfilesAsync() => TwoProfilesAsync(_ws);

    // profile-a and profile-b with several differences; the folder is left on profile-b. Built once per test run and copied.
    private static async Task<(RepoRef Repo, SortedDictionary<string, string> A, SortedDictionary<string, string> B)> TwoProfilesAsync(EngineWorkspace ws)
    {
        var (repo, pair) = await RepoTemplates.CopyAsync("checkout-profiles", ws, async template =>
        {
            var built = await BuildProfilesAsync(template);
            return (built.Repo, (built.A, built.B));
        });
        return (repo, pair.A, pair.B);
    }

    private static async Task<(RepoRef Repo, SortedDictionary<string, string> A, SortedDictionary<string, string> B)> BuildProfilesAsync(EngineWorkspace ws)
    {
        var repo = await ws.InitializedRepoAsync();
        var random = new Random(42);
        var blob = new byte[64 * 1024];
        random.NextBytes(blob);

        ws.Write(repo, "readme.txt", "version A\nline two\n");
        ws.Write(repo, "keep.txt", "same in both");
        ws.Write(repo, "only-a.txt", "A only");
        ws.Write(repo, "Data/blob.bin", blob);
        ws.Write(repo, _accent, "accent a");
        ws.Write(repo, "Data/sub dir/it's [draft] (1).txt", "tricky a");
        await ws.CaptureAsync(repo, "profile-a");
        var a = TreeSnapshot.Take(repo.WorkTree);

        await ws.GitWriteAsync(repo, "checkout", "--quiet", "-b", "profile-b");
        random.NextBytes(blob);
        ws.Write(repo, "readme.txt", "version B\n");
        ws.Delete(repo, "only-a.txt");
        ws.Write(repo, "only-b.txt", "B only");
        ws.Write(repo, "Data/blob.bin", blob);
        ws.Write(repo, "Data/new.pak", "pak b");
        ws.Write(repo, _accent, "accent b");
        await ws.CaptureAsync(repo, "profile-b");
        return (repo, a, TreeSnapshot.Take(repo.WorkTree));
    }
}
