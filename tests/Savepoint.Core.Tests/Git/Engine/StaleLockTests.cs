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
            if (invocation.Arguments[0] == "add")
            {
                File.WriteAllText(lockFile, string.Empty);
                removedDuringCapture = _ws.Engine.RemoveStaleLocks(repo.GitDir);
            }
        };

        await _ws.CaptureAsync(repo, "profile-1");

        Assert.Equal(0, removedDuringCapture);
        Assert.True(File.Exists(lockFile), "another operation's lock must not be removed while it may be live");
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
