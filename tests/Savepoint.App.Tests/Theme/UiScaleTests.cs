using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Savepoint.App.Features.Shell;
using Savepoint.App.Theme;
using Savepoint.App.Views;

namespace Savepoint.App.Tests.Theme;

// The player's UI scale multiplies the operating system's display scaling: every window and every popup draws larger, at once.
public class UiScaleTests
{
    private static ThemeService Theme => ((App)Application.Current!).Theme;

    // The application's theme service is shared by every test, so a test that scales puts it back.
    private static IDisposable Scaled(int percent)
    {
        Theme.SetUiScale(percent);
        return new Reset();
    }

    private sealed class Reset : IDisposable
    {
        public void Dispose() => Theme.SetUiScale(100);
    }

    private static double ScaleOf(Visual visual, Visual root) => visual.TransformToVisual(root)!.Value.M11;

    [AvaloniaFact]
    public void Nothing_is_scaled_until_a_scale_is_set()
    {
        var harness = new ThemeHarness();

        Assert.Equal(100, harness.Service.UiScalePercent);
        Assert.Equal(1.0, Assert.IsType<ScaleTransform>(harness.Value(ThemeKeys.UiScale)).ScaleX);
    }

    [AvaloniaFact]
    public void Setting_a_scale_publishes_it_as_a_transform_and_says_so()
    {
        var harness = new ThemeHarness();
        var raised = 0;
        harness.Service.UiScaleChanged += (_, _) => raised++;

        harness.Service.SetUiScale(150);

        var transform = Assert.IsType<ScaleTransform>(harness.Value(ThemeKeys.UiScale));
        Assert.Equal(1.5, transform.ScaleX);
        Assert.Equal(1.5, transform.ScaleY);
        Assert.Equal(150, harness.Service.UiScalePercent);
        Assert.Equal(1, raised);
    }

    [AvaloniaFact]
    public void Setting_the_scale_it_already_has_changes_nothing()
    {
        var harness = new ThemeHarness();
        harness.Service.SetUiScale(150);
        var before = harness.Value(ThemeKeys.UiScale);
        var raised = 0;
        harness.Service.UiScaleChanged += (_, _) => raised++;

        harness.Service.SetUiScale(150);

        Assert.Same(before, harness.Value(ThemeKeys.UiScale));
        Assert.Equal(0, raised);
    }

    [AvaloniaTheory]
    [InlineData(100, 100)]
    [InlineData(125, 125)]
    [InlineData(200, 200)]
    [InlineData(0, 100)]
    [InlineData(99, 100)]
    [InlineData(137, 125)]
    [InlineData(140, 150)]
    [InlineData(1000, 200)]
    [InlineData(-50, 100)]
    public void A_scale_that_is_not_one_of_the_steps_becomes_the_nearest_step(int asked, int expected)
    {
        Assert.Equal(expected, UiScale.Nearest(asked));
        var harness = new ThemeHarness();

        harness.Service.SetUiScale(asked);

        Assert.Equal(expected, harness.Service.UiScalePercent);
    }

    [AvaloniaFact]
    public void The_main_window_draws_its_content_at_the_chosen_scale_and_follows_a_change_while_open()
    {
        var window = new MainWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var view = Assert.Single(window.GetVisualDescendants().OfType<MainView>());
        Assert.Equal(1.0, ScaleOf(view, window), 3);
        var width = window.ClientSize.Width;

        using (Scaled(150))
        {
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1.5, ScaleOf(view, window), 3);
            Assert.InRange(view.Bounds.Width, (width / 1.5) - 1, (width / 1.5) + 1);
        }

        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1.0, ScaleOf(view, window), 3);
        window.Close();
    }

    [AvaloniaFact]
    public void The_main_window_comes_up_already_scaled_when_the_scale_was_set_before_it_opened()
    {
        using var scaled = Scaled(200);

        var window = new MainWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var view = Assert.Single(window.GetVisualDescendants().OfType<MainView>());
        Assert.Equal(2.0, ScaleOf(view, window), 3);
        window.Close();
    }

    [AvaloniaFact]
    public void The_main_windows_smallest_size_grows_with_the_scale_so_the_content_is_never_squeezed()
    {
        var window = new MainWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();

        using (Scaled(150))
        {
            Assert.Equal(640 * 1.5, window.MinWidth, 3);
            Assert.Equal(420 * 1.5, window.MinHeight, 3);
        }

        Assert.Equal(640, window.MinWidth);
        Assert.Equal(420, window.MinHeight);
        window.Close();
    }

    [Fact]
    public void The_smallest_size_never_exceeds_the_room_the_screen_leaves()
    {
        var baseSize = new Size(640, 420);
        var fullHd = new PixelRect(0, 0, 1920, 1040);

        // A 1080p screen at the system's 200% leaves 960x520 logical pixels: the 1280x840 a 200% scale asks for does not fit.
        var scaled = MainWindow.ScaledMinimum(baseSize, 200, fullHd, 2.0);
        Assert.Equal(920, scaled.Width);
        Assert.Equal(480, scaled.Height);

        // Roomy screens get the full multiple.
        Assert.Equal(new Size(960, 630), MainWindow.ScaledMinimum(baseSize, 150, fullHd, 1.0));

        // Never below the size at 100%, however small the screen.
        Assert.Equal(baseSize, MainWindow.ScaledMinimum(baseSize, 200, new PixelRect(0, 0, 300, 200), 1.0));

        // Without a known screen the multiple stands.
        Assert.Equal(new Size(800, 525), MainWindow.ScaledMinimum(baseSize, 125, null, 1.0));
    }

    [AvaloniaFact]
    public void A_popup_opened_after_a_scale_change_is_drawn_at_that_scale()
    {
        using var scaled = Scaled(175);
        var (window, view) = ShownMainWindow();
        var bubble = new Border { Width = 80, Height = 30 };

        OpenFlyout(view, bubble);

        // The popup is drawn in the window's overlay layer in the headless platform and in a window of its own elsewhere; either way the bubble is drawn at the scale once.
        Assert.Equal(1.75, ScaleOf(bubble, TopLevel.GetTopLevel(bubble)!), 3);
        window.Close();
    }

    [AvaloniaFact]
    public void A_popup_that_is_open_follows_a_change_of_scale()
    {
        var (window, view) = ShownMainWindow();
        var bubble = new Border { Width = 80, Height = 30 };
        OpenFlyout(view, bubble);
        Assert.Equal(1.0, ScaleOf(bubble, TopLevel.GetTopLevel(bubble)!), 3);

        using (Scaled(125))
        {
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1.25, ScaleOf(bubble, TopLevel.GetTopLevel(bubble)!), 3);
        }

        window.Close();
    }

    private static (MainWindow Window, MainView View) ShownMainWindow()
    {
        var window = new MainWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, Assert.Single(window.GetVisualDescendants().OfType<MainView>()));
    }

    private static void OpenFlyout(Control target, Control content)
    {
        FlyoutBase.SetAttachedFlyout(target, new Flyout { Content = content });
        FlyoutBase.ShowAttachedFlyout(target);
        Dispatcher.UIThread.RunJobs();
    }
}
