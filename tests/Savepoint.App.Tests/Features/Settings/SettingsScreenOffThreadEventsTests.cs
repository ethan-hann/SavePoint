using Avalonia.Headless.XUnit;
using Savepoint.App.Features.Common;
using Savepoint.App.Features.Settings;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.Common;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Settings;

// The registry raises job events from its own thread. These show the Settings screen on the real dispatcher with events that arrive
// after the call returned, so the Free up space row has to follow a run it did not start itself.
public class SettingsScreenOffThreadEventsTests
{
    [AvaloniaFact]
    public async Task A_free_up_space_run_that_ends_off_the_UI_thread_gives_the_row_back_and_dates_the_last_run()
    {
        var backend = new FakeBackend(ScenarioBuilder.Sample().WithSettings(SampleScenario.Settings with { Theme = ThemePreference.Dark })).WithDelivery(EventDelivery.Background);
        using var harness = await SettingsScreenViewTests.StartAsync(backend, dispatcher: new AvaloniaUiDispatcher());
        var storage = ((SettingsScreenViewModel)harness.Shell.CurrentScreen!).Storage;
        Assert.Contains(harness.VisibleTexts(), text => text?.Contains("You last freed up space on Jul 1.", StringComparison.Ordinal) == true);

        var started = FakeAssert.Ok(await backend.Settings.OptimizeAllStorageAsync());
        await backend.WhenEventsDeliveredAsync();

        Assert.True(await UiWait.UntilAsync(() => storage.IsOptimizing));

        backend.Jobs.Complete(started.JobId);
        await backend.WhenEventsDeliveredAsync();

        Assert.True(await UiWait.UntilAsync(() => !storage.IsOptimizing));
        Assert.True(await UiWait.UntilAsync(() => harness.VisibleTexts().Any(text => text?.Contains("You last freed up space on Jul 8.", StringComparison.Ordinal) == true)));
    }
}
