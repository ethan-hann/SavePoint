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

    // Where the fake is simpler than the real backend. Each side is pinned, so closing a gap in the fake fails here and the step
    // moves into the shared story.
    [Fact]
    public void The_places_the_fake_is_simpler_than_the_real_backend_are_pinned()
    {
        Assert.Equal(
            [
                "history after the first save: 0 points",
                "row stats after the first save: active=False saves=0",
                "storage used: False",
                "delete preview of the active profile: points=0 blocked=no",
                "delete of the active profile: deleted",
                "read with a cancelled token: success",
            ],
            runs.Fake.Gaps);
        Assert.Equal(
            [
                "history after the first save: 1 points",
                "row stats after the first save: active=True saves=1",
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
