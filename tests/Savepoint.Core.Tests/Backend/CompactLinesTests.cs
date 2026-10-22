using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.Core.Tests.Backend;

// What freeing up space writes into its details: the game, and what it freed.
public class CompactLinesTests
{
    private static IReadOnlyList<JobLine.Step> Steps(JobSnapshot job) => [.. job.RecentLines.OfType<JobLine.Step>()];

    // The step that says how much a game's space grew, as the result says it: nothing freed is its own sentence.
    private static JobLine.Step Outcome(string game, GameOptimizeOutcome outcome) =>
        outcome.BytesFreed is > 0
            ? Assert.IsType<JobLine.Step>(JobLine.ForStep(JobSteps.CompactFreed, ("game", game), ("bytes", outcome.BytesFreed.Value)))
            : Assert.IsType<JobLine.Step>(JobLine.ForStep(JobSteps.CompactNothingFreed, ("game", game)));

    [Fact]
    public async Task Freeing_up_space_for_one_game_names_the_game_and_what_it_freed()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        await workspace.CreateProfileAsync(game);

        var started = await workspace.Games.OptimizeStorageAsync(game.Id);
        var job = await workspace.WaitForJobAsync(started.Value!.JobId);

        var outcome = Assert.Single(Assert.IsType<OptimizeResult>(job.Result).Games);
        Assert.Equal([JobLine.ForStep(JobSteps.CompactGame, ("game", "Alpha")), Outcome("Alpha", outcome)], Steps(job));
    }

    [Fact]
    public async Task Freeing_up_space_for_every_game_names_each_one_in_turn()
    {
        using var workspace = new BackendWorkspace();
        var alpha = await workspace.AddGameAsync(workspace.NewGame("Alpha"));
        var beta = await workspace.AddGameAsync(workspace.NewGame("Beta", withSaves: false));
        await workspace.CreateProfileAsync(alpha);
        await workspace.CreateProfileAsync(beta);

        var started = await workspace.Settings.OptimizeAllStorageAsync();
        var job = await workspace.WaitForJobAsync(started.Value!.JobId);

        var outcomes = Assert.IsType<OptimizeResult>(job.Result).Games;
        Assert.Equal(
            [
                JobLine.ForStep(JobSteps.CompactGame, ("game", "Alpha")),
                Outcome("Alpha", outcomes[0]),
                JobLine.ForStep(JobSteps.CompactGame, ("game", "Beta")),
                Outcome("Beta", outcomes[1]),
            ],
            Steps(job));
    }

    [Fact]
    public async Task Freeing_up_space_that_nothing_uses_any_more_says_how_much_came_back()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync(workspace.NewGame(withSaves: false));
        await workspace.CreateProfileAsync(game);
        PlantUnreachableFile(Directory.EnumerateDirectories(workspace.DefaultStorageRoot, "install.git", SearchOption.AllDirectories).Single(), 400_000);

        var started = await workspace.Games.OptimizeStorageAsync(game.Id);
        var job = await workspace.WaitForJobAsync(started.Value!.JobId);

        var outcome = Assert.Single(Assert.IsType<OptimizeResult>(job.Result).Games);
        Assert.True(outcome.BytesFreed > 300_000, $"freed {outcome.BytesFreed}");
        Assert.Equal([JobLine.ForStep(JobSteps.CompactGame, ("game", "Alpha")), JobLine.ForStep(JobSteps.CompactFreed, ("game", "Alpha"), ("bytes", outcome.BytesFreed!.Value))], Steps(job));
    }

    // A file's content stored the way git stores it, with nothing pointing at it, as a profile's deleted snapshots leave behind.
    private static void PlantUnreachableFile(string gitDir, int size)
    {
        var content = System.Security.Cryptography.RandomNumberGenerator.GetBytes(size);
        byte[] stored = [.. System.Text.Encoding.ASCII.GetBytes($"blob {size}\0"), .. content];
        var id = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(stored)).ToLowerInvariant();
        var path = Path.Combine(gitDir, "objects", id[..2], id[2..]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var file = File.Create(path);
        using var deflate = new System.IO.Compression.ZLibStream(file, System.IO.Compression.CompressionLevel.Optimal);
        deflate.Write(stored);
    }

    [Fact]
    public async Task A_folder_that_could_not_be_optimized_is_named_and_nothing_is_said_to_be_freed()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        await workspace.CreateProfileAsync(game);

        // A drive with no room left refuses to reclaim space in both folders.
        workspace.Disk.FreePerPath = path => path.StartsWith(workspace.DefaultStorageRoot, StringComparison.OrdinalIgnoreCase) ? 1 : null;
        var started = await workspace.Games.OptimizeStorageAsync(game.Id);
        var job = await workspace.WaitForJobAsync(started.Value!.JobId);

        var steps = Steps(job);
        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Contains(JobLine.ForStep(JobSteps.CompactFolderFailed, ("game", "Alpha"), ("folder", RepoRole.Install)), steps);
        Assert.Contains(JobLine.ForStep(JobSteps.CompactFolderFailed, ("game", "Alpha"), ("folder", RepoRole.External)), steps);
        Assert.DoesNotContain(steps, step => step.MessageId is JobSteps.CompactFreed or JobSteps.CompactNothingFreed);
    }
}
