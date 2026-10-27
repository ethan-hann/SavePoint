using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Git;
using Savepoint.Core.Tests.Git;

namespace Savepoint.Core.Tests.Backend;

// A SavePoint that is killed while a reclaim runs can leave git running. The reclaim noted that git in the game's git directory, and the
// SavePoint that starts next waits for it before it writes to the game, so the packs it deletes are never under a new write.
public class InterruptedReclaimTests
{
    // A write or check that did not wait for the live git would finish within moments, and only waiting shows that it does not.
    private static readonly TimeSpan _waitWindow = TimeSpan.FromMilliseconds(750);

    private static async Task<(BackendWorkspace Workspace, GameDefinition Game, string GitDir)> WorldAsync()
    {
        var workspace = new BackendWorkspace();
        try
        {
            var game = await workspace.AddGameAsync();
            await workspace.CreateProfileAsync(game);
            var storage = await workspace.Backend.Services.Storage.GetAsync(workspace.Token);
            return (workspace, game, storage.GitDirectory(game.Id, RepoRole.Install));
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    [Fact(Timeout = 120_000)]
    public async Task The_first_write_to_a_game_waits_for_the_git_a_killed_reclaim_left_running()
    {
        var (workspace, game, gitDir) = await WorldAsync();
        using var _ = workspace;
        var runner = workspace.Backend.Services.Runner;
        var gone = await LiveOrphan.EndedAsync(runner, gitDir);
        var orphan = await LiveOrphan.StartAsync(runner, gitDir);
        ReclaimMarker.Write(gitDir, gone, orphan.Stamp);

        var write = workspace.Backend.Services.RunForGameAsync(game.Id, (_, _) => Task.FromResult(7), workspace.Token);
        var early = await Task.WhenAny(write, Task.Delay(_waitWindow, workspace.Token));
        var markerWhileWaiting = File.Exists(ReclaimMarker.PathIn(gitDir));
        await orphan.EndAsync();
        var result = await write.WaitAsync(TimeSpan.FromSeconds(30), workspace.Token);

        Assert.NotSame(write, early);
        Assert.True(markerWhileWaiting);
        Assert.Equal(7, result);
        Assert.False(File.Exists(ReclaimMarker.PathIn(gitDir)));
    }

    [Fact(Timeout = 120_000)]
    public async Task The_startup_check_waits_for_it_before_it_sweeps_the_games()
    {
        var (workspace, _, gitDir) = await WorldAsync();
        using var _ = workspace;
        var runner = workspace.Backend.Services.Runner;
        var gone = await LiveOrphan.EndedAsync(runner, gitDir);
        var orphan = await LiveOrphan.StartAsync(runner, gitDir);
        ReclaimMarker.Write(gitDir, gone, orphan.Stamp);
        var leftover = Path.Combine(gitDir, "objects", "pack", "tmp_pack_left_behind");
        await File.WriteAllTextAsync(leftover, "half a pack", workspace.Token);
        workspace.Restart();

        var check = workspace.AppStatus.CheckAsync(workspace.Token);
        var early = await Task.WhenAny(check, Task.Delay(_waitWindow, workspace.Token));
        var leftoverWhileWaiting = File.Exists(leftover);
        await orphan.EndAsync();
        var status = await check.WaitAsync(TimeSpan.FromSeconds(30), workspace.Token);

        Assert.NotSame(check, early);
        Assert.True(leftoverWhileWaiting);
        Assert.True(status.Ok, status.Error?.Detail);
        Assert.False(File.Exists(ReclaimMarker.PathIn(gitDir)));
        Assert.False(File.Exists(leftover));
    }

    [Fact(Timeout = 120_000)]
    public async Task The_startup_check_says_it_is_finishing_an_earlier_cleanup_before_it_starts_waiting()
    {
        var (workspace, _, gitDir) = await WorldAsync();
        using var _ = workspace;
        var runner = workspace.Backend.Services.Runner;
        var gone = await LiveOrphan.EndedAsync(runner, gitDir);
        var orphan = await LiveOrphan.StartAsync(runner, gitDir);
        ReclaimMarker.Write(gitDir, gone, orphan.Stamp);
        workspace.Restart();
        var told = new TaskCompletionSource<StartupProgress>(TaskCreationOptions.RunContinuationsAsynchronously);

        var check = workspace.AppStatus.CheckAsync(workspace.Token, new Progress<StartupProgress>(step => told.TrySetResult(step)));
        var first = await Task.WhenAny(told.Task, Task.Delay(5000, workspace.Token));
        var checkWasStillRunning = !check.IsCompleted;
        await orphan.EndAsync();
        await check.WaitAsync(TimeSpan.FromSeconds(30), workspace.Token);

        Assert.Same(told.Task, first);
        Assert.Equal(StartupProgress.FinishingEarlierCleanup, await told.Task);
        Assert.True(checkWasStillRunning);
    }

    [Fact(Timeout = 120_000)]
    public async Task The_startup_check_reports_nothing_when_no_cleanup_was_left_behind_or_the_one_left_has_ended()
    {
        var (workspace, _, gitDir) = await WorldAsync();
        using var _ = workspace;
        var runner = workspace.Backend.Services.Runner;
        var steps = new List<StartupProgress>();
        var progress = new SyncProgress(steps.Add);

        await workspace.AppStatus.CheckAsync(workspace.Token, progress);
        var gone = await LiveOrphan.EndedAsync(runner, gitDir);
        ReclaimMarker.Write(gitDir, gone, gone);
        workspace.Restart();
        await workspace.AppStatus.CheckAsync(workspace.Token, progress);

        Assert.Empty(steps);
        Assert.False(File.Exists(ReclaimMarker.PathIn(gitDir)));
    }

    private sealed class SyncProgress(Action<StartupProgress> report) : IProgress<StartupProgress>
    {
        public void Report(StartupProgress value) => report(value);
    }

    [Fact(Timeout = 120_000)]
    public async Task A_game_with_no_note_is_written_to_without_waiting()
    {
        var (workspace, game, gitDir) = await WorldAsync();
        using var _ = workspace;

        var result = await workspace.Backend.Services.RunForGameAsync(game.Id, (_, _) => Task.FromResult(7), workspace.Token).WaitAsync(TimeSpan.FromSeconds(10), workspace.Token);

        Assert.Equal(7, result);
        Assert.False(File.Exists(ReclaimMarker.PathIn(gitDir)));
    }
}
