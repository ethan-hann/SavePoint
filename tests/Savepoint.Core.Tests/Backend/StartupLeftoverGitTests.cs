using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Git;
using Savepoint.Core.Platform;
using Savepoint.Core.Tests.Git;
using Savepoint.Core.Tests.Launch;
using Savepoint.Core.Tests.Services;
using Savepoint.Core.Tests.Services.Exe;
using Savepoint.Testing;

namespace Savepoint.Core.Tests.Backend;

// The startup check removes the lock files a stopped SavePoint left, on the grounds that nothing of SavePoint's is running (D18). A git that
// outlived that SavePoint still uses them, so it is ended first, and when it cannot be the locks stay.
public sealed class StartupLeftoverGitTests : IDisposable
{
    private readonly TempFolder _temp = new("savepoint-startup-leftover-");
    private readonly RecordingDiagnosticLog _log = new();
    private StandInGitBundle? _bundle;

    public void Dispose()
    {
        _bundle?.Dispose();
        _temp.Dispose();
    }

    [Fact(Timeout = 120_000)]
    public async Task A_git_a_stopped_SavePoint_left_running_is_ended_before_its_locks_are_removed()
    {
        var (workspace, gitDir) = await WorldAsync(kill: null);
        using var _ = workspace;
        var leftover = await _bundle!.StartLeftoverAsync();
        var lockFile = Path.Combine(gitDir, "index.lock");
        await File.WriteAllTextAsync(lockFile, "held by a git that is still running", workspace.Token);
        workspace.Restart();

        var status = await workspace.AppStatus.CheckAsync(workspace.Token);

        Assert.True(status.Ok, status.Error?.Detail);
        Assert.False(FakeGameProcess.IsRunning(leftover), "the leftover git is still running");
        Assert.False(File.Exists(lockFile), "the lock it held was not removed once it was gone");
    }

    [Fact(Timeout = 120_000)]
    public async Task Locks_stay_while_a_leftover_git_is_still_running()
    {
        var (workspace, gitDir) = await WorldAsync(kill: _ => { });
        using var _ = workspace;
        var leftover = await _bundle!.StartLeftoverAsync();
        var lockFile = Path.Combine(gitDir, "index.lock");
        var temporaryPack = Path.Combine(gitDir, "objects", "pack", "tmp_pack_left_behind");
        Directory.CreateDirectory(Path.GetDirectoryName(temporaryPack)!);
        await File.WriteAllTextAsync(lockFile, "held by a git that is still running", workspace.Token);
        await File.WriteAllTextAsync(temporaryPack, "half a pack", workspace.Token);
        workspace.Restart();

        var status = await workspace.AppStatus.CheckAsync(workspace.Token);

        Assert.True(status.Ok, status.Error?.Detail);
        Assert.True(FakeGameProcess.IsRunning(leftover));
        Assert.True(File.Exists(lockFile), "a lock was removed under a git that is still running");
        Assert.True(File.Exists(temporaryPack), "a temporary pack was removed under a git that is still running");
        Assert.NotEmpty(_log.Containing("could not be ended"));
    }

    [Fact(Timeout = 120_000)]
    public async Task The_screen_is_told_an_earlier_cleanup_is_being_finished_while_a_leftover_is_waited_for()
    {
        var (workspace, _) = await WorldAsync(kill: null);
        using var _ = workspace;
        await _bundle!.StartLeftoverAsync();
        workspace.Restart();
        var steps = new List<StartupProgress>();

        await workspace.AppStatus.CheckAsync(workspace.Token, new SyncProgress(steps.Add));

        Assert.Equal([StartupProgress.FinishingEarlierCleanup], steps);
    }

    [Fact(Timeout = 120_000)]
    public async Task Locks_are_removed_as_before_when_no_git_was_left()
    {
        var (workspace, gitDir) = await WorldAsync(kill: null);
        using var _ = workspace;
        var lockFile = Path.Combine(gitDir, "index.lock");
        await File.WriteAllTextAsync(lockFile, "left by a killed git", workspace.Token);
        workspace.Restart();

        var status = await workspace.AppStatus.CheckAsync(workspace.Token);

        Assert.True(status.Ok, status.Error?.Detail);
        Assert.False(File.Exists(lockFile));
    }

    private async Task<(BackendWorkspace Workspace, string GitDir)> WorldAsync(Action<RunningProcess>? kill)
    {
        _bundle = new StandInGitBundle(_temp, "bundle");
        var short_ = TimeSpan.FromMilliseconds(400);
        var leftovers = new BundledGitLeftovers(
            FakeGameInstall.Inspector(),
            _bundle.Root,
            OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase,
            _log,
            GitProcessRegistry.Shared.Contains,
            short_,
            short_,
            TimeSpan.FromMilliseconds(50),
            kill);
        var workspace = new BackendWorkspace(options => options with { Leftovers = leftovers });
        try
        {
            var game = await workspace.AddGameAsync();
            await workspace.CreateProfileAsync(game);
            var storage = await workspace.Backend.Services.Storage.GetAsync(workspace.Token);
            return (workspace, storage.GitDirectory(game.Id, RepoRole.Install));
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    private sealed class SyncProgress(Action<StartupProgress> report) : IProgress<StartupProgress>
    {
        public void Report(StartupProgress value) => report(value);
    }
}
