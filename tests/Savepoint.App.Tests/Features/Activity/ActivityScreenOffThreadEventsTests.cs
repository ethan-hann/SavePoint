using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Savepoint.App.Composition;
using Savepoint.App.Features.Activity;
using Savepoint.App.Features.Common;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.Common;
using Savepoint.App.Tests.Features.History;
using static Savepoint.App.Tests.Fakes.SampleScenario;

namespace Savepoint.App.Tests.Features.Activity;

// The registry raises job events from its own thread, and the screen has to put each one on the UI thread. These run the screen on the
// real dispatcher with events that arrive after the call returned, off the test's thread, and then click in the list the way a reader does.
public class ActivityScreenOffThreadEventsTests
{
    [AvaloniaFact]
    public async Task Clicking_a_task_after_others_finished_off_the_UI_thread_selects_it_and_drops_the_open_one_that_the_registry_let_go()
    {
        var backend = new FakeBackend(ScenarioBuilder.Sample()).WithDelivery(EventDelivery.Background);
        var first = FakeAssert.Ok(await backend.Games.OptimizeStorageAsync(SkyrimId)).JobId;
        backend.Jobs.Complete(first);
        backend.Clock.Advance(TimeSpan.FromMinutes(1));
        using var harness = await ScreenHarness.StartAsync("activity", Factories(backend), NavDestination.Activity, backend, dispatcher: new AvaloniaUiDispatcher());
        var screen = (ActivityScreenViewModel)harness.Shell.CurrentScreen!;
        Assert.True(await UiWait.UntilAsync(() => screen.Rows.Count == 1));
        screen.SelectedRow = screen.Rows.Single(row => row.JobId == first);

        for (var i = 0; i < 5; i++)
        {
            var job = FakeAssert.Ok(await backend.Games.OptimizeStorageAsync(SkyrimId)).JobId;
            backend.Jobs.Complete(job);
            backend.Clock.Advance(TimeSpan.FromMinutes(1));
        }

        await backend.WhenEventsDeliveredAsync();

        // The open task stays while it is open, even though the registry no longer keeps it.
        Assert.True(await UiWait.UntilAsync(() => screen.Rows.Count == 6));
        Assert.Equal(first, screen.SelectedRow?.JobId);
        var chosen = screen.Rows[0];
        var list = harness.Find<ListBox>("JobList");
        list.ScrollIntoView(chosen);
        harness.Settle();
        var item = list.GetVisualDescendants().OfType<ListBoxItem>().Single(candidate => ReferenceEquals(candidate.DataContext, chosen));
        var at = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), harness.Window)!.Value;

        harness.Window.MouseDown(at, MouseButton.Left);
        harness.Window.MouseUp(at, MouseButton.Left);

        Assert.True(await UiWait.UntilAsync(() => screen.Rows.Count == 5));
        Assert.Same(chosen, screen.SelectedRow);
        Assert.DoesNotContain(screen.Rows, row => row.JobId == first);
    }

    private static Func<FeatureContext, IReadOnlyList<IScreenFactory>> Factories(FakeBackend backend) =>
        context => [new ActivityScreenFactory(new ActivityDependencies(context.Services.Jobs, context.Dialogs, context.Text, context.Dispatcher, backend.Clock))];
}
