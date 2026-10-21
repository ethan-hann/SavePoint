using Savepoint.Core.Api;
using Savepoint.Core.Git;
using Savepoint.Core.Services.Storage;

namespace Savepoint.Core.Tests.Backend;

public class OptimizeTests
{
    [Fact]
    public async Task Optimizing_one_game_runs_a_job_for_it_and_reports_each_folder()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        await workspace.CreateProfileAsync(game);

        var started = await workspace.Games.OptimizeStorageAsync(game.Id);
        var job = await workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(JobKind.Compact, job.Kind);
        Assert.Equal(game.Id, job.GameId);
        Assert.Equal(JobStatus.Succeeded, job.Status);
        var result = Assert.IsType<OptimizeResult>(job.Result);
        var outcome = Assert.Single(result.Games);
        Assert.Equal(game.Id, outcome.GameId);
        Assert.Equal(2, outcome.Folders.Count);
        Assert.All(outcome.Folders, folder => Assert.True(folder.Ok));
        Assert.NotNull(outcome.BytesFreed);
    }

    [Fact]
    public async Task Optimizing_everything_is_one_job_with_a_result_per_game_and_it_sets_the_optimized_date()
    {
        using var workspace = new BackendWorkspace();
        var alpha = await workspace.AddGameAsync(workspace.NewGame("Alpha"));
        var beta = await workspace.AddGameAsync(workspace.NewGame("Beta", withSaves: false));
        await workspace.CreateProfileAsync(alpha);
        await workspace.CreateProfileAsync(beta);

        var started = await workspace.Settings.OptimizeAllStorageAsync();
        var job = await workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Null(job.GameId);
        var result = Assert.IsType<OptimizeResult>(job.Result);
        Assert.Equal([alpha.Id, beta.Id], result.Games.Select(g => g.GameId));
        Assert.Equal([2, 1], result.Games.Select(g => g.Folders.Count));
        var settings = (await workspace.Settings.GetAsync(workspace.Token)).Value!;
        Assert.True(Math.Abs((DateTimeOffset.UtcNow - settings.LastOptimizedAt!.Value).TotalMinutes) < 5);
        Assert.Equal(settings.LastOptimizedAt, (await workspace.Settings.GetStorageSummaryAsync(workspace.Token)).Value!.LastOptimizedAt);
    }

    [Fact]
    public async Task Optimizing_a_library_with_no_games_succeeds_and_changes_no_date()
    {
        using var workspace = new BackendWorkspace();

        var started = await workspace.Settings.OptimizeAllStorageAsync();
        var job = await workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Empty(Assert.IsType<OptimizeResult>(job.Result).Games);
        Assert.Null((await workspace.Settings.GetAsync(workspace.Token)).Value!.LastOptimizedAt);
    }

    [Fact]
    public async Task Optimizing_an_unknown_game_is_an_error_and_starts_no_job()
    {
        using var workspace = new BackendWorkspace();

        var started = await workspace.Games.OptimizeStorageAsync("nope");

        Assert.False(started.Ok);
        Assert.Empty(workspace.Jobs.List());
    }

    [Fact]
    public async Task An_optimization_can_be_cancelled_and_says_so_without_calling_it_a_failure()
    {
        var reached = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync(workspace.NewGame(withSaves: false));
        await workspace.CreateProfileAsync(game);
        workspace.Disk.FreePerPath = path =>
        {
            if (path.StartsWith(workspace.DefaultStorageRoot, StringComparison.OrdinalIgnoreCase))
            {
                reached.Set();
                release.Wait(TimeSpan.FromSeconds(30));
            }

            return null;
        };

        var started = await workspace.Games.OptimizeStorageAsync(game.Id);
        Assert.True(reached.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
        await workspace.Jobs.CancelAsync(started.Value!.JobId);
        release.Set();
        var job = await workspace.WaitForJobAsync(started.Value.JobId);

        Assert.Equal(JobStatus.Cancelled, job.Status);
        Assert.Equal("error.compaction.cancelled", job.Error?.MessageId);
    }

    [Fact]
    public async Task A_capture_stops_an_automatic_optimization_of_the_same_game_to_go_first()
    {
        var reached = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync(workspace.NewGame(withSaves: false));
        var (profile, _) = await workspace.CreateProfileAsync(game);
        BackendWorkspace.WriteFile(Path.Combine(game.Install.Path, "a.txt"), "a");
        workspace.Disk.FreePerPath = path =>
        {
            if (path.StartsWith(workspace.DefaultStorageRoot, StringComparison.OrdinalIgnoreCase))
            {
                reached.Set();
                release.Wait(TimeSpan.FromSeconds(30));
            }

            return null;
        };
        var optimizing = new OptimizationService(workspace.Backend.Services).Start(game.Id, OptimizationMode.Routine, preemptible: true);
        Assert.True(reached.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));

        var capture = await workspace.Profiles.CaptureAsync(game.Id, profile.Id, "Cut in line");
        await Task.Delay(200, workspace.Token);
        release.Set();
        var optimization = await workspace.WaitForJobAsync(optimizing);
        var captured = await workspace.WaitForJobAsync(capture.Value!.JobId);

        Assert.Equal(JobStatus.Cancelled, optimization.Status);
        Assert.Equal("error.compaction.preempted", optimization.Error?.MessageId);
        Assert.Equal(JobStatus.Succeeded, captured.Status);
    }

    [Fact]
    public async Task A_manual_optimization_is_not_stopped_by_a_capture()
    {
        var reached = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync(workspace.NewGame(withSaves: false));
        var (profile, _) = await workspace.CreateProfileAsync(game);
        BackendWorkspace.WriteFile(Path.Combine(game.Install.Path, "a.txt"), "a");
        workspace.Disk.FreePerPath = path =>
        {
            if (path.StartsWith(workspace.DefaultStorageRoot, StringComparison.OrdinalIgnoreCase) && !release.IsSet)
            {
                reached.Set();
                release.Wait(TimeSpan.FromSeconds(30));
            }

            return null;
        };
        var optimizing = await workspace.Games.OptimizeStorageAsync(game.Id);
        Assert.True(reached.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));

        var capture = await workspace.Profiles.CaptureAsync(game.Id, profile.Id, "Waits its turn");
        await Task.Delay(200, workspace.Token);
        release.Set();
        var optimization = await workspace.WaitForJobAsync(optimizing.Value!.JobId);
        var captured = await workspace.WaitForJobAsync(capture.Value!.JobId);

        Assert.Equal(JobStatus.Succeeded, optimization.Status);
        Assert.Equal(JobStatus.Succeeded, captured.Status);
    }
}
