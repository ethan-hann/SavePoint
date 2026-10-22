using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Savepoint.App.Tests.Features.EndToEnd;
using Savepoint.App.Theme;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Theme;

public class ReducedMotionTests
{
    private const string _className = "reduced-motion";

    private static ThemeService Theme => ((App)Application.Current!).Theme;

    private static Color ColorOf(string brushKey)
    {
        Assert.True(Application.Current!.TryGetResource(brushKey, null, out var brush), $"No resource '{brushKey}'.");
        return ((SolidColorBrush)brush!).Color;
    }

    // The track is translucent, so the gap shows it over the window's surface.
    private static Color Over(Color top, Color bottom)
    {
        byte Mix(byte above, byte below) => (byte)Math.Round(((above * top.A) + (below * (255 - top.A))) / 255.0);
        return Color.FromRgb(Mix(top.R, bottom.R), Mix(top.G, bottom.G), Mix(top.B, bottom.B));
    }

    private static void AssertClose(Color expected, Color actual)
    {
        Assert.InRange(Math.Abs(expected.R - actual.R), 0, 2);
        Assert.InRange(Math.Abs(expected.G - actual.G), 0, 2);
        Assert.InRange(Math.Abs(expected.B - actual.B), 0, 2);
    }

    private static Window Show(Control content)
    {
        var window = new Window { Width = 260, Height = 160, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static Control? Part(ProgressBar bar, string name) =>
        bar.GetVisualDescendants().OfType<Control>().FirstOrDefault(control => control.Name == name);

    // Whether the moving segment is under an animation: it starts well off its resting place and returns to it when the animation stops.
    private static bool Sweeping(ProgressBar bar)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(5);
        Dispatcher.UIThread.RunJobs();
        return Part(bar, "IndeterminateProgressBarIndicator")?.RenderTransform?.Value.M31 is not (null or 0);
    }

    [AvaloniaFact]
    public void A_window_carries_the_reduced_motion_class_only_while_the_setting_is_on()
    {
        using var host = new ThemeHost(new TextBlock());
        Assert.DoesNotContain(_className, host.Window.Classes);

        Theme.ReduceMotion = true;
        Assert.Contains(_className, host.Window.Classes);

        Theme.ReduceMotion = false;
        Assert.DoesNotContain(_className, host.Window.Classes);
    }

    [AvaloniaFact]
    public void A_window_opened_while_the_setting_is_on_starts_with_the_class()
    {
        Window? late = null;
        try
        {
            Theme.ReduceMotion = true;
            late = Show(new TextBlock());

            Assert.Contains(_className, late.Classes);
        }
        finally
        {
            late?.Close();
            ThemeTestSupport.ResetApplicationTheme();
        }
    }

    [AvaloniaFact]
    public void The_indeterminate_sweep_runs_while_motion_is_on()
    {
        var bar = new ProgressBar { IsIndeterminate = true, Width = 200 };
        using var host = new ThemeHost(bar);

        Assert.True(Sweeping(bar));
    }

    [AvaloniaFact]
    public void The_indeterminate_sweep_holds_still_and_a_segmented_bar_stands_in_under_reduced_motion()
    {
        Window? window = null;
        try
        {
            Theme.ReduceMotion = true;
            var bar = new ProgressBar { IsIndeterminate = true, Width = 200 };
            window = Show(bar);

            Assert.False(Sweeping(bar));
            Assert.False(Part(bar, "IndeterminateProgressBarIndicator")?.IsEffectivelyVisible ?? false);
            Assert.False(Part(bar, "IndeterminateProgressBarIndicator2")?.IsEffectivelyVisible ?? false);
            Assert.True(Part(bar, "ReducedMotionIndicator")?.IsEffectivelyVisible ?? false);
        }
        finally
        {
            window?.Close();
            ThemeTestSupport.ResetApplicationTheme();
        }
    }

    [AvaloniaFact]
    public void Turning_reduced_motion_on_while_a_sweep_runs_stops_it()
    {
        var bar = new ProgressBar { IsIndeterminate = true, Width = 200 };
        using var host = new ThemeHost(bar);
        Assert.True(Sweeping(bar));

        Theme.ReduceMotion = true;

        Assert.False(Sweeping(bar));
        Assert.True(Part(bar, "ReducedMotionIndicator")?.IsEffectivelyVisible ?? false);
    }

    [AvaloniaFact]
    public void The_determinate_bar_is_unaffected_by_reduced_motion()
    {
        var bar = new ProgressBar { Minimum = 0, Maximum = 100, Value = 40, Width = 200 };
        using var host = new ThemeHost(bar);

        Theme.ReduceMotion = true;

        Assert.False(Part(bar, "ReducedMotionIndicator")?.IsEffectivelyVisible ?? false);
        Assert.True(Part(bar, "PART_Indicator")?.IsEffectivelyVisible ?? false);
    }

    [AvaloniaTheory]
    [InlineData(ThemePreference.Light)]
    [InlineData(ThemePreference.Dark)]
    public void The_still_bar_is_drawn_in_segments_with_gaps_that_show_the_track(ThemePreference mode)
    {
        var bar = new ProgressBar { IsIndeterminate = true, Width = 200 };
        Window? window = null;
        try
        {
            Theme.ReduceMotion = true;
            Theme.Apply(mode, AccentColorId.Blue);
            window = new Window { Width = 260, Height = 80, Content = bar };
            using var frame = ThemeTestSupport.Render(window);
            ThemeTestSupport.SaveFrame(frame, Path.Combine(ComposedApp.RenderFolder("motion"), $"indeterminate-reduced-motion-{mode.ToString().ToLowerInvariant()}.png"));

            // The bar is centered: 30px in from the left, five segments of about 37px with 4px gaps.
            var y = (int)(window.Bounds.Height / 2);
            var inSegment = ThemeTestSupport.PixelAt(frame, 30 + 18, y);
            var inGap = ThemeTestSupport.PixelAt(frame, 30 + 39, y);

            Assert.Equal(ColorOf("Md3PrimaryBrush"), inSegment);
            AssertClose(Over(ColorOf("Md3ProgressTrackBrush"), ColorOf("Md3SurfaceBrush")), inGap);
        }
        finally
        {
            window?.Close();
            ThemeTestSupport.ResetApplicationTheme();
        }
    }
}
