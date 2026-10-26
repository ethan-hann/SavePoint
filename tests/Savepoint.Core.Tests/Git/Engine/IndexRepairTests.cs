using Savepoint.Core.Errors;
using Savepoint.Core.Git;
using Savepoint.Testing;

namespace Savepoint.Core.Tests.Git.Engine;

// A power loss right after a snapshot or switch can leave git's index file empty, short or zero-filled, and every git command then
// fails. SavePoint sets the file aside and rebuilds it from the latest snapshot, which never touches the game's files.
[Collection(EngineCollections.F)]
public sealed class IndexRepairTests : IDisposable
{
    private const string _damagedId = "error.gitOperation.recordDamaged";

    private readonly EngineWorkspace _ws = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    private static string IndexOf(RepoRef repo) => Path.Combine(repo.GitDir, "index");

    private static string[] Backups(RepoRef repo) => Directory.GetFiles(repo.GitDir, "index.damaged-*");

    private static void Empty(RepoRef repo) => File.WriteAllBytes(IndexOf(repo), []);

    private static void ZeroFill(RepoRef repo) => File.WriteAllBytes(IndexOf(repo), new byte[new FileInfo(IndexOf(repo)).Length]);

    private static void Truncate(RepoRef repo) => File.WriteAllBytes(IndexOf(repo), File.ReadAllBytes(IndexOf(repo))[..20]);

    // The loose object a snapshot is stored as, removed the way a lost write would.
    private async Task RemoveObjectAsync(RepoRef repo, string id)
    {
        var path = Path.Combine(repo.GitDir, "objects", id[..2], id[2..]);
        Assert.True(File.Exists(path), "expected the snapshot to be stored as a loose object");
        File.SetAttributes(path, FileAttributes.Normal);
        File.Delete(path);
        Assert.False(await _ws.ObjectExistsAsync(repo, id));
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("zero-filled")]
    [InlineData("truncated")]
    public async Task A_snapshot_after_the_index_was_damaged_is_taken_and_the_game_files_are_untouched(string damage)
    {
        var (repo, _) = await _ws.ThreePointsRepoAsync();
        _ws.Write(repo, "c.txt", "new");
        var before = TreeSnapshot.Take(repo.WorkTree);
        Action damageIndex = damage switch
        {
            "empty" => () => Empty(repo),
            "zero-filled" => () => ZeroFill(repo),
            _ => () => Truncate(repo),
        };
        damageIndex();

        var failure = await Record.ExceptionAsync(() => _ws.CaptureAsync(repo, "profile-1", "After the power cut"));

        Assert.Null(failure);
        Assert.Equal(["a.txt", "b.txt", "c.txt"], (await _ws.TreeAsync(repo, "profile-1")).Order());
        Assert.Equal(before, TreeSnapshot.Take(repo.WorkTree));
        Assert.Single(Backups(repo));
    }

    [Fact]
    public async Task The_damaged_index_is_kept_under_a_new_name_beside_the_rebuilt_one()
    {
        var (repo, _) = await _ws.ThreePointsRepoAsync();
        var damaged = File.ReadAllBytes(IndexOf(repo))[..20];
        File.WriteAllBytes(IndexOf(repo), damaged);

        var failure = await Record.ExceptionAsync(() => _ws.CaptureAsync(repo, "profile-1", "After the power cut"));

        Assert.Null(failure);
        var backup = Assert.Single(Backups(repo));
        Assert.Equal(damaged, File.ReadAllBytes(backup));
        Assert.True(new FileInfo(IndexOf(repo)).Length > 20, "the index was rebuilt");
    }

    [Fact]
    public async Task A_switch_after_the_index_was_damaged_goes_through()
    {
        var (repo, points) = await _ws.ThreePointsRepoAsync();
        Empty(repo);

        var failure = await Record.ExceptionAsync(() => _ws.Engine.CheckoutCommitAsync(repo, points[0], ct: Ct));

        Assert.Null(failure);
        Assert.Equal("one", await File.ReadAllTextAsync(_ws.FileOf(repo, "a.txt"), Ct));
        Assert.False(File.Exists(_ws.FileOf(repo, "b.txt")));
    }

    [Fact]
    public async Task A_first_snapshot_that_was_cut_off_before_anything_was_recorded_is_taken_afresh()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "one");
        Empty(repo);
        var before = TreeSnapshot.Take(repo.WorkTree);

        var failure = await Record.ExceptionAsync(() => _ws.CaptureAsync(repo, "profile-1", "First"));

        Assert.Null(failure);
        Assert.Equal(["a.txt"], await _ws.TreeAsync(repo, "profile-1"));
        Assert.Equal(before, TreeSnapshot.Take(repo.WorkTree));
        Assert.Single(Backups(repo));
    }

    [Fact]
    public async Task Reading_the_status_repairs_the_index_once_even_though_it_asks_git_twice_at_the_same_time()
    {
        var (repo, _) = await _ws.ThreePointsRepoAsync();
        _ws.Write(repo, "c.txt", "new");
        Empty(repo);

        RepoStatus? status = null;
        var failure = await Record.ExceptionAsync(async () => status = await _ws.Engine.GetStatusAsync(repo, Ct));

        Assert.Null(failure);
        Assert.NotNull(status);
        Assert.False(status.IsClean);
        Assert.Single(Backups(repo));
    }

    [Fact]
    public async Task When_the_latest_snapshot_cannot_be_read_the_error_says_so_and_nothing_is_changed()
    {
        var (repo, _) = await _ws.ThreePointsRepoAsync();
        _ws.Write(repo, "c.txt", "new");
        await RemoveObjectAsync(repo, (await _ws.ResolveAsync(repo, "profile-1"))!);
        Empty(repo);
        var before = TreeSnapshot.Take(repo.WorkTree);

        var error = await Assert.ThrowsAsync<RepositoryDamagedException>(() => _ws.Engine.ListCaptureCandidatesAsync(repo, [], Ct));

        Assert.Equal(_damagedId, error.MessageId);
        Assert.Equal(before, TreeSnapshot.Take(repo.WorkTree));
        Assert.Empty(Backups(repo));
        Assert.Equal(0, new FileInfo(IndexOf(repo)).Length);
    }

    [Fact]
    public async Task A_profile_whose_record_was_emptied_is_not_mistaken_for_one_with_nothing_saved()
    {
        var (repo, _) = await _ws.ThreePointsRepoAsync();
        File.WriteAllBytes(Path.Combine(repo.GitDir, "refs", "heads", "profile-1"), []);
        Empty(repo);

        await Assert.ThrowsAsync<RepositoryDamagedException>(() => _ws.Engine.ListCaptureCandidatesAsync(repo, [], Ct));

        Assert.Empty(Backups(repo));
        Assert.Equal(0, new FileInfo(IndexOf(repo)).Length);
    }

    [Fact]
    public async Task A_profile_kept_in_the_packed_refs_has_its_index_rebuilt_from_it()
    {
        var (repo, _) = await _ws.ThreePointsRepoAsync();
        await _ws.GitWriteAsync(repo, "pack-refs", "--all", "--prune");
        Assert.False(File.Exists(Path.Combine(repo.GitDir, "refs", "heads", "profile-1")), "the ref should now be packed");
        Empty(repo);

        await _ws.Engine.ListCaptureCandidatesAsync(repo, [], Ct);

        Assert.Equal(["a.txt", "b.txt"], (await _ws.GitOutAsync(repo, "ls-files")).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Order());
    }

    [Fact]
    public async Task A_snapshot_cannot_be_taken_either_when_the_latest_one_cannot_be_read()
    {
        var (repo, _) = await _ws.ThreePointsRepoAsync();
        _ws.Write(repo, "c.txt", "new");
        await RemoveObjectAsync(repo, (await _ws.ResolveAsync(repo, "profile-1"))!);
        Empty(repo);
        var before = TreeSnapshot.Take(repo.WorkTree);

        var error = await Record.ExceptionAsync(() => _ws.CaptureAsync(repo, "profile-1", "After the power cut"));

        var damaged = Assert.IsType<RepositoryDamagedException>(error);
        Assert.Equal(_damagedId, damaged.MessageId);
        Assert.Equal(before, TreeSnapshot.Take(repo.WorkTree));
    }

    [Fact]
    public async Task An_index_that_is_damaged_again_right_after_the_repair_is_reported_instead_of_repaired_forever()
    {
        var (repo, _) = await _ws.ThreePointsRepoAsync();
        Empty(repo);
        var repaired = false;
        _ws.OnInvoke = invocation =>
        {
            if (invocation.Arguments[0] == "read-tree")
            {
                repaired = true;
            }
            else if (repaired)
            {
                Empty(repo);
            }
        };

        var error = await Assert.ThrowsAsync<RepositoryDamagedException>(() => _ws.Engine.GetStatusAsync(repo, Ct));

        Assert.Equal(_damagedId, error.MessageId);

        // Two reads run side by side, so each may have asked once; neither asks again.
        Assert.InRange(_ws.Invocations.Count(invocation => invocation.Arguments[0] == "read-tree"), 1, 2);
    }

    [Fact]
    public async Task A_repair_is_written_to_the_log_with_the_name_of_the_set_aside_file()
    {
        var (repo, _) = await _ws.ThreePointsRepoAsync();
        Empty(repo);
        var log = new RecordingDiagnosticLog();
        var engine = new GitEngine(new GitRunner(_ws.Binary, new GitRunnerOptions { Log = log }), _ws.PathSafety, _ws.Space.Probe, _ws.StorageRoot);

        await engine.GetStatusAsync(repo, Ct);

        var entry = Assert.Single(log.Entries, e => e.Message.Contains("index", StringComparison.OrdinalIgnoreCase) && e.Level != LogLevel.Error && e.Message.Contains("rebuilt", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(Path.GetFileName(Assert.Single(Backups(repo))), entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_repair_that_could_not_be_done_is_written_to_the_log()
    {
        var (repo, _) = await _ws.ThreePointsRepoAsync();
        await RemoveObjectAsync(repo, (await _ws.ResolveAsync(repo, "profile-1"))!);
        Empty(repo);
        var log = new RecordingDiagnosticLog();
        var engine = new GitEngine(new GitRunner(_ws.Binary, new GitRunnerOptions { Log = log }), _ws.PathSafety, _ws.Space.Probe, _ws.StorageRoot);

        await Assert.ThrowsAsync<RepositoryDamagedException>(() => engine.ListCaptureCandidatesAsync(repo, [], Ct));

        Assert.Contains(log.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("could not be rebuilt", StringComparison.Ordinal));
    }
}
