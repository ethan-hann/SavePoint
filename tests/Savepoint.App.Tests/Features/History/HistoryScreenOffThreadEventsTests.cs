using Avalonia.Headless.XUnit;
using Savepoint.App.Composition;
using Savepoint.App.Features.Common;
using Savepoint.App.Features.History;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.Common;
using static Savepoint.App.Tests.Fakes.SampleScenario;

namespace Savepoint.App.Tests.Features.History;

// The registry raises job events from its own thread. These show the History screen on the real dispatcher with events that arrive
// after the call returned, while the reader has already switched the screen to the list.
public class HistoryScreenOffThreadEventsTests
{
    [AvaloniaFact]
    public async Task A_snapshot_that_finishes_off_the_UI_thread_joins_the_list_the_reader_has_open()
    {
        var backend = new FakeBackend(ScenarioBuilder.Sample()).WithDelivery(EventDelivery.Background);
        using var harness = await ScreenHarness.StartAsync(
            "history",
            context => [new HistoryScreenFactory(FeatureComposition.HistoryDependenciesOf(context, backend.Clock))],
            NavDestination.History,
            backend,
            dispatcher: new AvaloniaUiDispatcher());
        var screen = (HistoryScreenViewModel)harness.Shell.CurrentScreen!;
        screen.SelectedViewOption = screen.ViewOptions[1];
        harness.Settle();
        Assert.Contains("Skyrim Special Edition · 8 snapshots", harness.VisibleTexts());
        var loaded = FakeAssert.Ok(await backend.Profiles.GetRowStatsAsync(SkyrimId)).Single(stats => stats.IsActive).ProfileId;

        var job = FakeAssert.Ok(await backend.Profiles.CaptureAsync(SkyrimId, loaded, "Off the UI thread")).JobId;
        backend.Jobs.Complete(job);
        await backend.WhenEventsDeliveredAsync();

        Assert.True(await UiWait.UntilAsync(() => harness.VisibleTexts().Contains("Skyrim Special Edition · 9 snapshots")));
        Assert.Contains("Off the UI thread", harness.VisibleTexts());
    }
}
