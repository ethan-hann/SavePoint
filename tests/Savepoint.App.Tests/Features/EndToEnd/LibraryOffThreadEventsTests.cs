using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Savepoint.App.Features.Common;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.Common;
using Savepoint.App.Tests.Theme;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.EndToEnd;

// A job that ends reports from the registry's thread, and the shell reads the library again. These run the shell and the library shelf
// on the real dispatcher, with the click on a card landing after that refresh.
public class LibraryOffThreadEventsTests
{
    [AvaloniaFact]
    public async Task A_job_that_ends_off_the_UI_thread_refreshes_the_shelf_and_a_click_on_a_card_still_selects_it()
    {
        var backend = new FakeBackend(ScenarioBuilder.Sample().WithSettings(SampleScenario.Settings with { Layout = ShellLayout.Library })).WithDelivery(EventDelivery.Background);
        using var app = await ComposedApp.StartWithViewAsync(backend, layout: ShellLayout.Library, dispatcher: new AvaloniaUiDispatcher());
        var writes = new UiThreadWrites().Watch(app.Shell).WatchList(app.Shell.Games).Watch(app.Shell.JobStatus);
        var reads = ListReads(backend);
        var job = FakeAssert.Ok(await backend.Profiles.CaptureAsync(SampleScenario.SkyrimId, SampleScenario.ModdedId, "Off the UI thread")).JobId;
        backend.Jobs.Complete(job);
        await backend.WhenEventsDeliveredAsync();

        Assert.True(await UiWait.UntilAsync(() => ListReads(backend) > reads));
        app.Settle();
        var card = app.Find<ListBox>("ShelfGames").GetVisualDescendants().OfType<ListBoxItem>().ElementAt(1);
        var at = LayoutAudit.BoundsIn(app.Window, card).Center;
        app.Window.MouseDown(at, MouseButton.Left);
        app.Window.MouseUp(at, MouseButton.Left);
        app.Settle();

        Assert.Equal(SampleScenario.SatisfactoryId, app.Shell.SelectedGame?.Id);
        Assert.Empty(writes.Offenders);
    }

    private static int ListReads(FakeBackend backend) => backend.Calls.To<IGamesApi>(nameof(IGamesApi.ListAsync)).Count;
}
