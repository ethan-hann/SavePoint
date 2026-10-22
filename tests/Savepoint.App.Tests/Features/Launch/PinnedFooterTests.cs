using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.EndToEnd;
using Savepoint.App.Tests.Theme;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Launch;

// With every section showing, the launch dialog is taller than a small window. Its buttons are the way out of it, so they stay
// where they can be seen and the body above them scrolls, with a scroll bar that does not fade away.
public class PinnedFooterTests
{
    private static async Task<LaunchViewHarness> StartAllSectionsAsync(double width, double height)
    {
        var source = FakeAssert.Ok(await FakeBackend.Mockup().Launch.GetPreviewAsync(MockupScenario.SkyrimId, LaunchRig.SurvivalTip));
        var preview = source with
        {
            Install = source.Install with { Dirty = true, DirtyFileCount = 12, DirtyOwnerProfileId = MockupScenario.SurvivalId },
            External = source.External! with { DirtyOwnerProfileId = null },
            TargetIsOlderPoint = true,
        };
        var backend = new FakeBackend(ScenarioBuilder.Mockup().WithSettings(MockupScenario.Settings with { Theme = ThemePreference.Dark }).WithLaunchPreview(preview));
        return await LaunchViewHarness.StartAsync(backend, ThemePreference.Dark, width, height);
    }

    private static void AssertInView(Window window, Control control, string what)
    {
        var shown = LayoutAudit.BoundsIn(window, control);
        Assert.True(
            shown.Top >= 0 && shown.Bottom <= window.Bounds.Height && shown.Left >= 0 && shown.Right <= window.Bounds.Width,
            $"{what} is at {shown} in a {window.Bounds.Width}x{window.Bounds.Height} window.");
    }

    [AvaloniaTheory]
    [InlineData(1040, 660)]
    [InlineData(960, 460)]
    [InlineData(910, 432)]
    public async Task The_launch_dialogs_buttons_stay_in_view_while_its_body_scrolls(double width, double height)
    {
        using var harness = await StartAllSectionsAsync(width, height);
        var begin = harness.Flow.BeginAsync(MockupScenario.SkyrimId, LaunchRig.SurvivalTip);
        harness.Settle();
        harness.Render($"2b-launch-dialog-all-sections-{width}x{height}");
        var cancel = harness.Find<Button>("CancelButton");
        var switchButton = harness.Find<Button>("SwitchButton");
        var body = harness.Find<ItemsControl>("UnsavedList").GetVisualAncestors().OfType<ScrollViewer>().First();

        AssertInView(harness.Window, cancel, "Cancel");
        AssertInView(harness.Window, switchButton, "Switch and play");
        var before = LayoutAudit.BoundsIn(harness.Window, switchButton);

        body.ScrollToEnd();
        harness.Settle();

        AssertInView(harness.Window, switchButton, "Switch and play after scrolling");
        Assert.Equal(before, LayoutAudit.BoundsIn(harness.Window, switchButton));
        Assert.False(body.AllowAutoHide, "The scroll bar fades away, so nothing says there is more above the buttons.");
        Assert.True(body.Viewport.Height >= 96, $"The body shows only {body.Viewport.Height:0}px.");

        harness.Dialogs.RequestDismissCurrent();
        await begin;
    }

    public static TheoryData<string, string> DialogsAtScaledSizes()
    {
        var data = new TheoryData<string, string>();
        foreach (var state in AppStates.Dialogs.Select(row => row.Data).Append("launch-dialog-saving"))
        {
            foreach (var (name, _, _) in LayoutAudit.ScaledScreens)
            {
                data.Add(state, name);
            }
        }

        return data;
    }

    [AvaloniaTheory]
    [MemberData(nameof(DialogsAtScaledSizes))]
    public async Task Every_dialogs_buttons_are_in_view_at_a_scaled_size(string state, string screen)
    {
        var (_, width, height) = LayoutAudit.ScaledScreens.Single(size => size.Name == screen);
        using var app = await AppStates.OpenAsync(state);
        app.Window.Width = width;
        app.Window.Height = height;
        app.Settle();

        var chrome = app.Find<Border>("Chrome");
        var hidden = chrome.GetVisualDescendants().OfType<Button>()
            .Where(button => button is not RepeatButton && button.IsEffectivelyVisible && button.Name?.StartsWith("PART_", StringComparison.Ordinal) != true)
            .Where(button => !button.GetVisualAncestors().OfType<ScrollViewer>().Any(scroller => scroller.Name != "DialogScroll"))
            .Select(button => (button, shown: LayoutAudit.BoundsIn(app.Window, button)))
            .Where(item => item.shown.Bottom > height || item.shown.Top < 0)
            .Select(item => $"{item.button.Name ?? item.button.Content} at {item.shown}")
            .ToList();

        Assert.True(hidden.Count == 0, $"{state} at {screen} ({width}x{height}) has buttons out of view: {string.Join("; ", hidden)}");
    }
}
