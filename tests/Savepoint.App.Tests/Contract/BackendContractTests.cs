namespace Savepoint.App.Tests.Contract;

// The fake backend is what every screen test is written against, so it has to say what the real one says.
public class BackendContractTests(ContractRuns runs) : IClassFixture<ContractRuns>
{
    [Fact]
    public void The_real_backend_tells_the_same_first_run_story_as_the_fake()
    {
        var expected = runs.Fake.Shared;
        var actual = runs.Real.Shared;

        // Compared step by step, so a failure names the step where the two part ways.
        var differences = expected.Zip(actual, (fakeSaw, realSaw) => (fakeSaw, realSaw))
            .Where(pair => pair.fakeSaw != pair.realSaw)
            .Select(pair => $"fake: {pair.fakeSaw}{Environment.NewLine}real: {pair.realSaw}")
            .ToList();
        Assert.True(differences.Count == 0 && expected.Count == actual.Count, string.Join(Environment.NewLine + Environment.NewLine, differences));
    }

    // A story whose launch failed to start in both backends would still match, so the steps that prove it ran are named here.
    [Fact]
    public void The_shared_story_plays_a_whole_launch()
    {
        foreach (var story in new[] { runs.Fake.Shared, runs.Real.Shared })
        {
            Assert.Contains("start launch: job started", story);
            Assert.Contains(story, line => line.StartsWith("session while the game runs: Running ", StringComparison.Ordinal));
            Assert.Contains(story, line => line.StartsWith("session after the game exits: Exited ", StringComparison.Ordinal));
            Assert.Contains("session after skip: Completed profile=profile#1 older=False completion=Skip result=True", story);
            Assert.Contains("second start while it runs: error error.launch.alreadyRunning", story);
            Assert.Contains("discard while it runs: error error.gameService.gameRunning", story);
            Assert.Contains("discard while it waits for the choice: error error.gameService.gameRunning", story);
        }
    }

    // The steps that prove the stopped switch and the save after play really ran in both backends, since a story that stopped early would still match.
    [Fact]
    public void The_shared_story_stops_a_switch_finishes_it_and_saves_the_play_after_it()
    {
        foreach (var story in new[] { runs.Fake.Shared, runs.Real.Shared })
        {
            Assert.Contains("preview before the switch: install unfinished=False dirty=False heading for nothing; saves unfinished=False dirty=False", story);
            Assert.Contains("session after the switch was stopped: Failed profile=profile#3 older=False completion=none result=True", story);
            Assert.Contains("preview after the switch was stopped: install unfinished=True dirty=True heading for the tip of profile#3; saves unfinished=False dirty=False", story);
            Assert.Contains("start the recorded target: job started", story);
            Assert.Contains("preview after the switch was finished: install unfinished=False dirty=False heading for nothing; saves unfinished=False dirty=False", story);
            Assert.Contains("complete with a new save: started", story);
            Assert.Contains("session after the new save: Completed profile=profile#3 older=False completion=CaptureNew result=False", story);
            Assert.Contains("what the new save recorded: Install:ok, External:ok point=named", story);
            Assert.Contains("session error after the new save: none", story);
        }
    }

    // Where the fake is simpler than the real backend. Each side is pinned, so closing a gap in the fake fails here and the step
    // moves into the shared story.
    [Fact]
    public void The_places_the_fake_is_simpler_than_the_real_backend_are_pinned()
    {
        Assert.Equal(
            [
                "choices after play with one save: CaptureNew,Skip",
                "history after the first save: 0 points",
                "row stats after the first save: active=False snapshots=0",
                "storage used: False",
                "delete preview of the active profile: points=0 blocked=no",
                "delete of the active profile: deleted",
                "read with a cancelled token: success",
            ],
            runs.Fake.Gaps);
        Assert.Equal(
            [
                "choices after play with one save: CaptureNew,UpdateExisting,Skip",
                "history after the first save: 1 points",
                "row stats after the first save: active=True snapshots=1",
                "storage used: True",
                "delete preview of the active profile: points=1 blocked=error.gameService.profileActive",
                "delete of the active profile: error error.gameService.profileActive",
                "read with a cancelled token: error error.operation.cancelled",
            ],
            runs.Real.Gaps);
    }
}

// The scenario runs once against each backend and both tests read the transcripts, since the real one costs a few git processes.
public sealed class ContractRuns : IAsyncLifetime
{
    internal ScenarioTranscript Fake { get; private set; } = new([], []);

    internal ScenarioTranscript Real { get; private set; } = new([], []);

    public async ValueTask InitializeAsync()
    {
        var token = TestContext.Current.CancellationToken;
        await using var fake = new FakeContractHarness();
        Fake = await ContractScenario.RunAsync(fake, token);
        await using var real = new RealContractHarness();
        Real = await ContractScenario.RunAsync(real, token);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
