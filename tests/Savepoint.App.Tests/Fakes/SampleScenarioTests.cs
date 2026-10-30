using static Savepoint.App.Tests.Fakes.FakeAssert;
using static Savepoint.App.Tests.Fakes.SampleScenario;

namespace Savepoint.App.Tests.Fakes;

// What the screens show of the sample data is checked by the screens' own tests; these two guard how the fake builds on it.
public class SampleScenarioTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Each_row_counts_the_snapshots_History_lists_for_that_profile()
    {
        var backend = FakeBackend.Sample();

        var stats = Ok(await backend.Profiles.GetRowStatsAsync(SkyrimId, Ct));

        foreach (var row in stats)
        {
            var listed = Ok(await backend.History.GetAsync(SkyrimId, row.ProfileId, Ct)).Points.Count;
            Assert.Equal(listed, row.SnapshotCount);
        }
    }
}
