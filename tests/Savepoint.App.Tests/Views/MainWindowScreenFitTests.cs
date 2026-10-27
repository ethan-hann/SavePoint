using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Savepoint.App.Theme;
using Savepoint.App.Views;

namespace Savepoint.App.Tests.Views;

// The window's smallest size and place follow the screen it is on and the interface size: the screens keep the room they were built
// for, and a window that has to grow never runs off the screen. The screen is injected, since the headless platform has one fixed screen.
public class MainWindowScreenFitTests
{
    private static readonly MainWindow.ScreenRoom _big = new(new PixelRect(0, 0, 3840, 2160), 1.0);
    private static readonly MainWindow.ScreenRoom _fullHd = new(new PixelRect(0, 0, 1920, 1040), 1.0);

    // 1080p at the system's own 200%: 960x520 logical pixels, which holds the 640x420 screens at 100% and nothing more.
    private static readonly MainWindow.ScreenRoom _fullHdAt200 = new(new PixelRect(0, 0, 1920, 1040), 2.0);

    private static ThemeService Theme => ((App)Application.Current!).Theme;

    private static MainWindow OpenOn(MainWindow.ScreenRoom screen, int percent)
    {
        Theme.SetUiScale(percent);
        var window = new MainWindow { ScreenSource = () => screen };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static void Finish(MainWindow window)
    {
        window.Close();
        Theme.SetUiScale(UiScale.Default);
    }

    // The window's outline in the pixels of the screen it is placed on (the headless window itself always renders at 1.0).
    private static PixelRect Outline(MainWindow window, MainWindow.ScreenRoom screen)
    {
        var size = window.FrameSize ?? window.ClientSize;
        return new PixelRect(window.Position, new PixelSize((int)Math.Ceiling(size.Width * screen.Scaling), (int)Math.Ceiling(size.Height * screen.Scaling)));
    }

    private static void AssertInside(PixelRect inner, PixelRect outer)
    {
        Assert.True(inner.X >= outer.X && inner.Y >= outer.Y && inner.Right <= outer.Right && inner.Bottom <= outer.Bottom, $"{inner} is not inside {outer}.");
    }

    [AvaloniaFact]
    public void The_smallest_size_follows_the_window_to_a_screen_with_less_room()
    {
        var window = OpenOn(_big, 150);
        try
        {
            Assert.Equal(960, window.MinWidth);
            Assert.Equal(150, Theme.UiScalePercent);

            window.ScreenSource = () => _fullHdAt200;
            window.Position = new PixelPoint(20, 20);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(100, Theme.UiScalePercent);
            Assert.Equal(640, window.MinWidth);
            Assert.Equal(420, window.MinHeight);

            window.ScreenSource = () => _big;
            window.Position = new PixelPoint(40, 40);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(150, Theme.UiScalePercent);
            Assert.Equal(960, window.MinWidth);
            Assert.Equal(630, window.MinHeight);
        }
        finally
        {
            Finish(window);
        }
    }

    [AvaloniaFact]
    public void A_window_that_grows_for_a_larger_size_is_moved_to_stay_inside_the_working_area()
    {
        var window = OpenOn(_fullHd, 100);
        try
        {
            window.Position = new PixelPoint(800, 300);
            Dispatcher.UIThread.RunJobs();

            Theme.SetUiScale(200);
            Dispatcher.UIThread.RunJobs();

            Assert.True(window.ClientSize.Width >= 1280 && window.ClientSize.Height >= 840, $"The window did not grow: {window.ClientSize}.");
            AssertInside(Outline(window, _fullHd), _fullHd.WorkArea);
        }
        finally
        {
            Finish(window);
        }
    }

    [AvaloniaFact]
    public void A_window_opens_centered_at_the_size_the_saved_interface_size_needs()
    {
        var window = OpenOn(_big, 200);
        try
        {
            Assert.True(window.ClientSize.Width >= 1280 && window.ClientSize.Height >= 840, $"The window opened at {window.ClientSize}.");
            var outline = Outline(window, _big);
            AssertInside(outline, _big.WorkArea);
            Assert.InRange(outline.Center.X, _big.WorkArea.Center.X - 2, _big.WorkArea.Center.X + 2);
            Assert.InRange(outline.Center.Y, _big.WorkArea.Center.Y - 2, _big.WorkArea.Center.Y + 2);
        }
        finally
        {
            Finish(window);
        }
    }

    [AvaloniaFact]
    public void A_saved_size_the_first_screen_has_no_room_for_opens_at_the_largest_that_fits_inside_it()
    {
        var window = OpenOn(_fullHdAt200, 200);
        try
        {
            Assert.Equal(100, Theme.UiScalePercent);
            Assert.Equal(640, window.MinWidth);
            var outline = Outline(window, _fullHdAt200);
            AssertInside(outline, _fullHdAt200.WorkArea);
            Assert.InRange(outline.Center.X, _fullHdAt200.WorkArea.Center.X - 2, _fullHdAt200.WorkArea.Center.X + 2);
            Assert.InRange(outline.Center.Y, _fullHdAt200.WorkArea.Center.Y - 2, _fullHdAt200.WorkArea.Center.Y + 2);
        }
        finally
        {
            Finish(window);
        }
    }

    [AvaloniaFact]
    public void Closing_the_window_leaves_no_screen_limit_on_the_interface_size()
    {
        var window = OpenOn(_fullHdAt200, 200);

        Finish(window);

        Assert.Equal(200, Theme.LargestFittingUiScale);
    }
}
