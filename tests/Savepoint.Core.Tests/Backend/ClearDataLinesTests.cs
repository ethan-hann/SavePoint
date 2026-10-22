using Savepoint.Core.Api;

namespace Savepoint.Core.Tests.Backend;

// What clearing SavePoint's data writes into its details: each game, each stage, and what could not be removed.
public class ClearDataLinesTests
{
    private static IReadOnlyList<JobLine.Step> Steps(JobSnapshot job) => [.. job.RecentLines.OfType<JobLine.Step>()];

    [Fact]
    public async Task Clearing_names_each_game_and_each_stage_in_order()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        await workspace.CreateProfileAsync(game);
        await workspace.WaitForIdleAsync();

        var started = await workspace.Settings.ClearAllDataAsync();
        var job = await workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(
            [
                JobLine.ForStep(JobSteps.ClearStart, ("games", 1)),
                JobLine.ForStep(JobSteps.ClearGame, ("game", "Alpha")),
                JobLine.ForStep(JobSteps.ClearFolders, ("count", 1)),
                JobLine.ForStep(JobSteps.ClearSettings),
                JobLine.ForStep(JobSteps.ClearArtwork),
                JobLine.ForStep(JobSteps.ClearLogs),
                JobLine.ForStep(JobSteps.ClearDone, ("left", 0)),
            ],
            Steps(job));
    }

    [Fact]
    public async Task Something_that_stays_is_named_and_counted_at_the_end()
    {
        using var workspace = new BackendWorkspace();
        await workspace.AddGameAsync();

        // A folder shaped like a game's storage whose "git directory" holds something that git never writes stays where it is.
        var foreign = Path.Combine(workspace.DefaultStorageRoot, "foreign", "install.git");
        BackendWorkspace.WriteFile(Path.Combine(foreign, "family-photos.zip"), "somebody's files");

        var started = await workspace.Settings.ClearAllDataAsync();
        var job = await workspace.WaitForJobAsync(started.Value!.JobId);

        var steps = Steps(job);
        Assert.Equal(JobStatus.Partial, job.Status);
        Assert.Contains(JobLine.ForStep(JobSteps.ClearNotRemoved, ("path", foreign)), steps);
        Assert.Equal(JobLine.ForStep(JobSteps.ClearDone, ("left", 1)), steps[^1]);
    }

    [Fact]
    public async Task Clearing_a_library_with_no_games_says_so_in_its_first_step()
    {
        using var workspace = new BackendWorkspace();

        var started = await workspace.Settings.ClearAllDataAsync();
        var job = await workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(JobLine.ForStep(JobSteps.ClearStart, ("games", 0)), Steps(job).FirstOrDefault());
        Assert.DoesNotContain(Steps(job), step => step.MessageId == JobSteps.ClearGame);
    }
}
