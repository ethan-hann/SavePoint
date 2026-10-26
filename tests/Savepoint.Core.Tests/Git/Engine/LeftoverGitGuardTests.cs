using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// A git an earlier, stopped SavePoint left running, and that the startup check could not end, still uses the repository's files.
// While it runs, nothing that git may be using is removed, cleared or rewritten.
public sealed class LeftoverGitGuardTests : IDisposable
{
    private readonly bool[] _running = [true];
    private readonly EngineWorkspace _ws;

    public LeftoverGitGuardTests()
    {
        _ws = new EngineWorkspace(options => options with { LeftoverGitRunning = () => _running[0] });
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    [Fact]
    public async Task Locks_and_temporary_files_stay_before_a_write_while_the_leftover_runs_and_go_once_it_is_gone()
    {
        var repo = await _ws.InitializedRepoAsync();
        var lockFile = Path.Combine(repo.GitDir, "index.lock");
        var temporaryPack = Path.Combine(repo.GitDir, "objects", "pack", "tmp_pack_left_behind");
        Directory.CreateDirectory(Path.GetDirectoryName(temporaryPack)!);
        await File.WriteAllTextAsync(lockFile, string.Empty, Ct);
        await File.WriteAllTextAsync(temporaryPack, "half a pack", Ct);

        await _ws.Engine.RepackAsync(repo, OptimizationMode.Routine, Ct);

        Assert.True(File.Exists(lockFile), "a lock was removed under a git that is still running");
        Assert.True(File.Exists(temporaryPack), "a temporary pack was removed under a git that is still running");

        _running[0] = false;
        await _ws.Engine.RepackAsync(repo, OptimizationMode.Routine, Ct);

        Assert.False(File.Exists(lockFile));
        Assert.False(File.Exists(temporaryPack));
    }

    [Fact]
    public async Task A_temporary_pack_is_not_cleared_to_make_room_while_the_leftover_runs_and_is_once_it_is_gone()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");
        var temporaryPack = Path.Combine(repo.GitDir, "objects", "pack", "tmp_pack_x");
        Directory.CreateDirectory(Path.GetDirectoryName(temporaryPack)!);
        await File.WriteAllTextAsync(temporaryPack, "half a pack", Ct);

        // The volume has room only once the temporary pack is gone.
        _ws.Space.PerPath = _ => File.Exists(temporaryPack) ? 10 : null;

        await Assert.ThrowsAsync<DiskSpaceException>(() => _ws.CaptureAsync(repo));

        Assert.True(File.Exists(temporaryPack), "a temporary pack was removed under a git that is still running");

        _running[0] = false;
        var outcome = await _ws.CaptureAsync(repo);

        Assert.True(outcome.Changed);
        Assert.False(File.Exists(temporaryPack));
    }

    [Fact]
    public async Task The_startup_cleanup_removes_nothing_while_the_leftover_runs()
    {
        var repo = await _ws.InitializedRepoAsync();
        var lockFile = Path.Combine(repo.GitDir, "index.lock");
        await File.WriteAllTextAsync(lockFile, string.Empty, Ct);

        var removed = _ws.Engine.RemoveStaleLocks(repo.GitDir);

        Assert.Equal(0, removed);
        Assert.True(File.Exists(lockFile));

        _running[0] = false;

        Assert.True(_ws.Engine.RemoveStaleLocks(repo.GitDir) > 0);
        Assert.False(File.Exists(lockFile));
    }

    [Fact]
    public async Task A_reclaim_is_refused_while_the_leftover_runs_and_goes_ahead_once_it_is_gone()
    {
        var (repo, _) = await _ws.ThreePointsRepoAsync();

        await Assert.ThrowsAsync<GitOperationException>(() => _ws.Engine.RepackAsync(repo, OptimizationMode.Reclaim, Ct));

        _running[0] = false;
        await _ws.Engine.RepackAsync(repo, OptimizationMode.Reclaim, Ct);
    }

    [Fact]
    public async Task Clearing_records_is_refused_while_the_leftover_runs_and_goes_ahead_once_it_is_gone()
    {
        var repo = await _ws.InitializedRepoAsync();

        await Assert.ThrowsAsync<GitOperationException>(() => _ws.Engine.ClearExactAsync(repo, [], Ct));

        _running[0] = false;
        Assert.Empty(await _ws.Engine.ClearExactAsync(repo, [], Ct));
    }
}
