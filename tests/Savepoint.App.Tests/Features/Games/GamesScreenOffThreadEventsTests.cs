using Avalonia.Headless.XUnit;
using Savepoint.App.Features.Common;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.Common;
using static Savepoint.App.Tests.Fakes.SampleScenario;

namespace Savepoint.App.Tests.Features.Games;

// The registry raises job events from its own thread. These show the Games screen on the real dispatcher with events that arrive
// after the call returned, so the rows have to follow a snapshot they did not start themselves.
public class GamesScreenOffThreadEventsTests
{
    [AvaloniaFact]
    public async Task Every_row_waits_while_a_snapshot_runs_and_is_free_again_when_its_end_arrives_off_the_UI_thread()
    {
        var backend = FakeBackend.Sample().WithDelivery(EventDelivery.Background);
        using var harness = await GamesViewHarness.StartAsync(backend, dispatcher: new AvaloniaUiDispatcher());
        var screen = harness.Rig.Screen;
        var writes = new UiThreadWrites().Watch(screen).WatchList(screen.Profiles);
        var loaded = screen.Profiles.Single(row => row.IsActive);
        Assert.All(screen.Profiles, row => Assert.False(row.SnapshotBusy));

        var job = FakeAssert.Ok(await backend.Profiles.CaptureAsync(SkyrimId, loaded.Id, "Off the UI thread")).JobId;
        await backend.WhenEventsDeliveredAsync();

        Assert.True(await UiWait.UntilAsync(() => screen.Profiles.All(row => row.SnapshotBusy)));

        backend.Jobs.Complete(job);
        await backend.WhenEventsDeliveredAsync();

        Assert.True(await UiWait.UntilAsync(() => screen.Profiles.All(row => !row.SnapshotBusy)));
        Assert.Empty(writes.Offenders);
    }
}
