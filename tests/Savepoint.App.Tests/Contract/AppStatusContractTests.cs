namespace Savepoint.App.Tests.Contract;

// The startup and recovery screens (68 tests) are driven by the fake's startup answers, so the fake has to give the answers the real checks give.
public class AppStatusContractTests(AppStatusRuns runs) : IClassFixture<AppStatusRuns>
{
    [Fact]
    public void The_real_startup_checks_and_recovery_choices_answer_as_the_fake_does()
    {
        var differences = runs.Fake.Zip(runs.Real, (fakeSaw, realSaw) => (fakeSaw, realSaw))
            .Where(pair => pair.fakeSaw != pair.realSaw)
            .Select(pair => $"fake: {pair.fakeSaw}{Environment.NewLine}real: {pair.realSaw}")
            .ToList();
        Assert.True(differences.Count == 0 && runs.Fake.Count == runs.Real.Count, string.Join(Environment.NewLine + Environment.NewLine, differences) + $"{Environment.NewLine}fake had {runs.Fake.Count} steps, real {runs.Real.Count}.");
    }

    // A story that stopped early or never found a corrupt file would still match, so the steps that prove it ran are named here.
    [Fact]
    public void The_startup_story_finds_a_corrupt_catalog_and_a_lost_storage_folder_and_gets_past_both()
    {
        foreach (var story in new[] { runs.Fake, runs.Real })
        {
            Assert.Contains(story, line => line.StartsWith("check with a games file that has a backup: git ready; catalog in the catalog folder=True, corrupt=[games.json backup=games.json.bak error.catalog.corrupt]; storage not checked", StringComparison.Ordinal));
            Assert.Contains(story, line => line.StartsWith("check after the restore: git ready; catalog in the catalog folder=True, corrupt=[]; storage reachable=True at the default folder", StringComparison.Ordinal));
            Assert.Contains(story, line => line.StartsWith("check after the storage folder was moved: git ready; catalog in the catalog folder=True, corrupt=[]; storage reachable=False at the default folder error=error.storage.unreachable", StringComparison.Ordinal));
            Assert.Contains("use a folder that holds no history: error error.storage.noHistoryHere", story);
            Assert.Contains("use the folder that holds the history: taken: the moved folder", story);
            Assert.Contains("start empty: taken: the default folder", story);
        }
    }
}

// Both backends run the story once; the real one costs a few git processes.
public sealed class AppStatusRuns : IAsyncLifetime
{
    internal IReadOnlyList<string> Fake { get; private set; } = [];

    internal IReadOnlyList<string> Real { get; private set; } = [];

    public async ValueTask InitializeAsync()
    {
        var token = TestContext.Current.CancellationToken;
        Fake = await AppStatusScenario.RunAsync(() => new FakeContractHarness(), token);
        Real = await AppStatusScenario.RunAsync(() => new RealContractHarness(), token);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
