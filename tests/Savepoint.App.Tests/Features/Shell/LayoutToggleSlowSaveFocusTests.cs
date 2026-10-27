using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.Shell;

// Focus follows an arrow-key layout switch while the save runs, and the end of the save never pulls it back from where the player put it.
public class LayoutToggleSlowSaveFocusTests
{
    private static FakeBackend InLibraryLayout() =>
        new(ScenarioBuilder.Sample().WithSettings(SampleScenario.Settings with { Layout = ShellLayout.Library }));

    private static ListBox VisibleToggle(ShellViewHarness harness) =>
        harness.All<LayoutToggleView>().Single(view => view.IsEffectivelyVisible).FindControl<ListBox>("Toggle")!;

    private static IInputElement? Focused(ShellViewHarness harness) => harness.Window.FocusManager!.GetFocusedElement();

    private static void PressArrow(ShellViewHarness harness, Key key, PhysicalKey physical)
    {
        harness.Window.KeyPress(key, RawInputModifiers.None, physical, null);
        harness.Settle();
    }

    // The save's continuation runs after the gate opens, so let it and the UI jobs it posts run.
    private static async Task FinishAsync(ShellViewHarness harness)
    {
        for (var i = 0; i < 10; i++)
        {
            await Task.Yield();
            harness.Settle();
        }
    }

    private static void FocusSelectedSegment(ShellViewHarness harness)
    {
        var list = VisibleToggle(harness);
        list.ContainerFromIndex(list.SelectedIndex)!.Focus(NavigationMethod.Tab);
        harness.Settle();
    }

    [AvaloniaFact]
    public async Task Focus_the_player_moved_elsewhere_during_a_slow_save_stays_there()
    {
        var backend = InLibraryLayout();
        using var harness = await ShellViewHarness.StartAsync(backend);
        var release = new TaskCompletionSource();
        backend.Settings.SaveGate = () => release.Task;
        FocusSelectedSegment(harness);

        PressArrow(harness, Key.Left, PhysicalKey.ArrowLeft);
        Assert.Equal(ShellLayout.Sidebar, harness.Shell.Layout);
        var elsewhere = harness.Find<ListBox>("NavList").ContainerFromIndex(1)!;
        elsewhere.Focus(NavigationMethod.Tab);
        harness.Settle();
        Assert.Same(elsewhere, Focused(harness));

        release.SetResult();
        await FinishAsync(harness);

        Assert.Same(elsewhere, Focused(harness));
    }

    [AvaloniaFact]
    public async Task A_save_that_succeeds_leaves_focus_on_the_toggle_shown_in_the_new_layout()
    {
        var backend = InLibraryLayout();
        using var harness = await ShellViewHarness.StartAsync(backend);
        var release = new TaskCompletionSource();
        backend.Settings.SaveGate = () => release.Task;
        FocusSelectedSegment(harness);

        PressArrow(harness, Key.Left, PhysicalKey.ArrowLeft);
        release.SetResult();
        await FinishAsync(harness);

        var focused = Assert.IsAssignableFrom<Visual>(Focused(harness));
        Assert.Same(VisibleToggle(harness), focused.FindAncestorOfType<ListBox>());
        Assert.Equal(0, VisibleToggle(harness).SelectedIndex);
    }

    [AvaloniaFact]
    public async Task A_save_that_fails_puts_the_layout_back_and_focus_on_the_toggle_now_shown()
    {
        var backend = InLibraryLayout();
        using var harness = await ShellViewHarness.StartAsync(backend);
        var release = new TaskCompletionSource();
        backend.Settings.SaveGate = () => release.Task;
        backend.FailNext<ISettingsApi>(nameof(ISettingsApi.SaveAsync), ErrorInfo.From(new InvalidOperationException("The settings could not be saved.")));
        FocusSelectedSegment(harness);

        PressArrow(harness, Key.Left, PhysicalKey.ArrowLeft);
        release.SetResult();
        await FinishAsync(harness);

        Assert.Equal(ShellLayout.Library, harness.Shell.Layout);
        var visible = VisibleToggle(harness);
        Assert.Equal(1, visible.SelectedIndex);
        var focused = Assert.IsAssignableFrom<Visual>(Focused(harness));
        Assert.Same(visible, focused.FindAncestorOfType<ListBox>());
    }
}
