using Savepoint.Core.Api;

namespace Savepoint.Core.Tests.Backend;

// Activity lists what finished before SavePoint was last closed, and Clear all data takes that list away with the rest.
public class ActivityHistoryTests
{
    [Fact(Timeout = 120_000)]
    public async Task A_snapshot_that_finished_is_still_in_Activity_after_the_backend_restarts_on_the_same_folders()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var (_, job) = await workspace.CreateProfileAsync(game);
        await workspace.WaitForIdleAsync();

        workspace.Restart();

        var restored = Assert.Single(workspace.Jobs.List(), listed => listed.JobId == job.JobId);
        Assert.Equal(JobStatus.Succeeded, restored.Status);
        Assert.Equal(JobKind.Capture, restored.Kind);
        Assert.Equal(game.Id, restored.GameId);
        Assert.All(workspace.Jobs.List(), listed => Assert.NotEqual(JobStatus.Running, listed.Status));
    }

    [Fact(Timeout = 120_000)]
    public async Task Clear_all_data_removes_the_saved_list_so_a_restart_lists_nothing()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        await workspace.CreateProfileAsync(game);
        await workspace.WaitForIdleAsync();
        Assert.NotEmpty(workspace.Jobs.List());

        var started = await workspace.Settings.ClearAllDataAsync();
        var cleared = await workspace.WaitForJobAsync(started.Value!.JobId, TimeSpan.FromSeconds(120));
        workspace.Restart();

        Assert.Equal(JobStatus.Succeeded, cleared.Status);
        Assert.Empty(workspace.Jobs.List());
    }
}
