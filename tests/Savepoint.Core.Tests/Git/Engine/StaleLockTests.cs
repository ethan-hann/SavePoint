using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

[Collection(EngineCollections.D)]
public sealed class StaleLockTests : IDisposable
{
    private readonly EngineWorkspace _ws = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    [Fact]
    public async Task The_lock_files_a_killed_git_leaves_are_removed_when_nothing_is_running()
    {
        var (repo, _) = await _ws.ThreePointsRepoAsync();
        var locks = new[]
        {
            Path.Combine(repo.GitDir, "index.lock"),
            Path.Combine(repo.GitDir, "HEAD.lock"),
            Path.Combine(repo.GitDir, "refs", "heads", "profile-1.lock"),
        };
        foreach (var path in locks)
        {
            await File.WriteAllTextAsync(path, string.Empty, Ct);
        }

        var removed = _ws.Engine.RemoveStaleLocks(repo.GitDir);

        Assert.Equal(3, removed);
        Assert.All(locks, path => Assert.False(File.Exists(path), path));
        Assert.True(File.Exists(Path.Combine(repo.GitDir, "HEAD")));
        Assert.True(File.Exists(Path.Combine(repo.GitDir, "refs", "heads", "profile-1")));
    }

    [Fact]
    public async Task A_classification_index_a_stopped_question_left_behind_is_removed_whatever_its_name()
    {
        var repo = await _ws.InitializedRepoAsync();
        var leftovers = new[] { "savepoint-classify-0123456789abcdef.index", "savepoint-classify-0123456789abcdef.index.lock", "savepoint-classify-fedcba9876543210.index" }
            .Select(name => Path.Combine(repo.GitDir, name))
            .ToArray();
        foreach (var path in leftovers)
        {
            await File.WriteAllTextAsync(path, string.Empty, Ct);
        }

        var removed = _ws.Engine.RemoveStaleLocks(repo.GitDir);

        Assert.Equal(3, removed);
        Assert.All(leftovers, path => Assert.False(File.Exists(path), path));
    }
    [Fact]
    public async Task With_no_lock_nothing_is_removed()
    {
        var repo = await _ws.InitializedRepoAsync();

        Assert.Equal(0, _ws.Engine.RemoveStaleLocks(repo.GitDir));
    }

    [Fact]
    public async Task A_folder_that_is_not_a_git_directory_is_left_alone()
    {
        var folder = Path.Combine(_ws.Root, "other");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "index.lock"), "mine", Ct);

        Assert.Equal(0, _ws.Engine.RemoveStaleLocks(folder));
        Assert.Equal(0, _ws.Engine.RemoveStaleLocks(Path.Combine(_ws.Root, "missing")));

        Assert.True(File.Exists(Path.Combine(folder, "index.lock")));
    }

    [Fact]
    public async Task A_lock_is_not_touched_while_this_engine_is_running_git_on_the_repository()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");
        var lockFile = Path.Combine(repo.GitDir, "refs", "heads", "other.lock");
        var removedDuringCapture = -1;
        _ws.OnInvoke = invocation =>
        {
            if (invocation.Arguments.Contains("add"))
            {
                File.WriteAllText(lockFile, string.Empty);
                removedDuringCapture = _ws.Engine.RemoveStaleLocks(repo.GitDir);
            }
        };

        await _ws.CaptureAsync(repo, "profile-1");

        Assert.Equal(0, removedDuringCapture);
        Assert.True(File.Exists(lockFile), "another operation's lock must not be removed while it may be live");
    }

    // What a git killed while it wrote a pack leaves behind: a temporary pack that can be hundreds of megabytes, and loose-object temp files.
    private static (string Pack, string Object) LeaveTemporaryFiles(RepoRef repo, int packBytes = 300_000)
    {
        var pack = Path.Combine(repo.GitDir, "objects", "pack", "tmp_pack_Ab12Cd");
        var loose = Path.Combine(repo.GitDir, "objects", "ab", "tmp_obj_Xy34Zq");
        Directory.CreateDirectory(Path.GetDirectoryName(pack)!);
        Directory.CreateDirectory(Path.GetDirectoryName(loose)!);
        File.WriteAllBytes(pack, new byte[packBytes]);
        File.WriteAllBytes(loose, new byte[50]);
        return (pack, loose);
    }

    [Fact]
    public async Task The_temporary_files_a_killed_git_leaves_are_removed_before_the_next_write()
    {
        var repo = await _ws.InitializedRepoAsync();
        var (pack, loose) = LeaveTemporaryFiles(repo);
        _ws.Write(repo, "a.txt", "a");

        await _ws.CaptureAsync(repo, "profile-1");

        Assert.False(File.Exists(pack), "a temporary pack nothing will ever finish");
        Assert.False(File.Exists(loose));
    }

    [Fact]
    public async Task The_temporary_files_are_counted_with_the_locks_when_they_are_removed_by_hand()
    {
        var repo = await _ws.InitializedRepoAsync();
        var (pack, loose) = LeaveTemporaryFiles(repo);
        await File.WriteAllTextAsync(Path.Combine(repo.GitDir, "index.lock"), string.Empty, Ct);

        Assert.Equal(3, _ws.Engine.RemoveStaleLocks(repo.GitDir));

        Assert.False(File.Exists(pack));
        Assert.False(File.Exists(loose));
    }

    [Fact]
    public async Task A_temporary_pack_is_not_touched_while_this_engine_is_running_git_on_the_repository()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");
        var pack = Path.Combine(repo.GitDir, "objects", "pack", "tmp_pack_Live01");
        Directory.CreateDirectory(Path.GetDirectoryName(pack)!);
        var removed = -1;
        _ws.OnInvoke = invocation =>
        {
            if (invocation.Arguments.Contains("add"))
            {
                File.WriteAllBytes(pack, new byte[1000]);
                removed = _ws.Engine.RemoveStaleLocks(repo.GitDir);
            }
        };

        await _ws.CaptureAsync(repo, "profile-1");

        Assert.Equal(0, removed);
        Assert.True(File.Exists(pack), "a pack another operation is writing must not be removed while it may be live");
    }

    [Fact]
    public async Task The_size_of_a_repository_counts_what_git_calls_garbage_so_a_leftover_temporary_pack_shows()
    {
        var repo = await _ws.InitializedRepoAsync();
        var before = await _ws.Engine.GetRepoSizeAsync(repo, Ct);

        LeaveTemporaryFiles(repo, packBytes: 409_600);

        Assert.True(await _ws.Engine.GetRepoSizeAsync(repo, Ct) - before >= 400_000);
    }

    [Fact]
    public async Task A_lock_is_cleared_after_the_operation_that_was_running_has_finished()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");
        await _ws.CaptureAsync(repo, "profile-1");
        var lockFile = Path.Combine(repo.GitDir, "index.lock");
        await File.WriteAllTextAsync(lockFile, string.Empty, Ct);

        Assert.Equal(1, _ws.Engine.RemoveStaleLocks(repo.GitDir));
        Assert.False(File.Exists(lockFile));
    }
}

public sealed class InFlightTableTests
{
    [Fact]
    public void An_operation_on_its_own_is_alone()
    {
        var table = new InFlightTable();

        using var scope = table.Enter("/store/a.git");

        Assert.True(scope.IsAlone);
        Assert.True(table.IsBusy("/store/a.git"));
    }

    [Fact]
    public void Two_operations_on_one_directory_are_not_alone_until_one_leaves()
    {
        var table = new InFlightTable();
        using var first = table.Enter("/store/a.git");
        var second = table.Enter("/store/a.git");

        Assert.False(first.IsAlone);
        Assert.False(second.IsAlone);
        second.Dispose();
        second.Dispose();

        Assert.True(first.IsAlone);
    }

    [Fact]
    public void Different_directories_do_not_count_against_each_other()
    {
        var table = new InFlightTable();
        using var first = table.Enter("/store/a.git");
        using var second = table.Enter("/store/b.git");

        Assert.True(first.IsAlone);
        Assert.True(second.IsAlone);
        Assert.False(table.IsBusy("/store/c.git"));
    }

    [Fact]
    public void A_directory_is_idle_again_when_every_operation_has_left()
    {
        var table = new InFlightTable();
        table.Enter("/store/a.git").Dispose();

        Assert.False(table.IsBusy("/store/a.git"));
    }
}
