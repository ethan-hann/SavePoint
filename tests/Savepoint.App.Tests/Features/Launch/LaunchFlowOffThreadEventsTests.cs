using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Savepoint.App.Features.Common;
using Savepoint.App.Features.Launch;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.Common;
using Savepoint.Core.Api;

namespace Savepoint.App.Tests.Features.Launch;

// The launch component raises session events from its own threads. These run the launch flow in the real window on the real dispatcher,
// with session events that arrive after each call returned, and then use the dialog the last event put up.
public class LaunchFlowOffThreadEventsTests
{
    [AvaloniaFact]
    public async Task The_post_play_choices_appear_once_the_session_events_arrive_off_the_UI_thread_and_answer_to_the_keyboard()
    {
        var backend = FakeBackend.Sample().WithDelivery(EventDelivery.Background);
        using var harness = await LaunchViewHarness.StartAsync(backend, dispatcher: new AvaloniaUiDispatcher());

        await harness.Flow.BeginAsync(SampleScenario.SkyrimId, LaunchRig.ModdedTip);
        backend.Launch.ReportRunning(SampleScenario.SkyrimId, SampleScenario.RunningSince);
        backend.Launch.ReportExited(SampleScenario.SkyrimId, SampleScenario.PlayDuration, SampleScenario.NewSaveFiles);
        await backend.WhenEventsDeliveredAsync();

        Assert.True(await UiWait.UntilAsync(() => harness.Dialogs.Stack.OfType<PostPlayDialogViewModel>().Any()));
        var dialog = harness.Dialogs.Stack.OfType<PostPlayDialogViewModel>().Single();
        var items = harness.Find<ListBox>("ChoiceList").GetVisualDescendants().OfType<ListBoxItem>().ToList();
        items[0].Focus(NavigationMethod.Tab);
        harness.Settle();

        harness.Window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        harness.Settle();

        Assert.Equal(CompletionChoice.UpdateExisting, dialog.SelectedChoice.Kind);
    }
}
