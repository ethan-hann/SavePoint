using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Settings;
using Savepoint.App.Features.Shell;
using Savepoint.App.Localization;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Theme;
using Savepoint.App.Theme;
using Savepoint.App.Views;

namespace Savepoint.App.Tests.Views;

// On a small screen the interface offers only the sizes the screens still fit in, and Settings stays usable at the largest of them, so
// the control that undoes a size is always reachable. Writes PNGs to render-output/small-screen/ (gitignored).
public class MainWindowSmallScreenTests
{
    private static ThemeService Theme => ((App)Application.Current!).Theme;

    private static string OutputFolder => Path.Combine(ThemeTestSupport.RepoRoot(), "render-output", "small-screen");

    private static async Task<(MainWindow Window, MainWindowViewModel Main)> OpenSettingsAsync(MainWindow.ScreenRoom screen, int savedPercent)
    {
        var backend = new FakeBackend(ScenarioBuilder.Sample().WithSettings(SampleScenario.Settings with { UiScalePercent = savedPercent }));
        Theme.SetUiScale(savedPercent);
        var main = AppComposition.Create(
            new AppServices(backend.Games, backend.Profiles, backend.Launch, backend.Settings, backend.History, backend.Jobs, backend.AppStatus, backend.Artwork),
            new UiFormatter(LocalizationCatalog.Current),
            new ThemeServiceApplier(Theme),
            () => null,
            new ImmediateUiDispatcher(),
            new ManualTimeProvider());
        var window = new MainWindow { DataContext = main, ScreenSource = () => screen };
        window.Show();
        await main.InitializeAsync();
        Dispatcher.UIThread.RunJobs();
        main.Shell!.Navigate(NavDestination.Settings);
        Dispatcher.UIThread.RunJobs();

        // The tightest case: the window at the smallest size it will go to on this screen.
        window.Width = window.MinWidth;
        window.Height = window.MinHeight;
        Dispatcher.UIThread.RunJobs();
        return (window, main);
    }

    private static void Finish(MainWindow window, MainWindowViewModel main)
    {
        main.Dispose();
        window.Close();
        Theme.SetUiScale(UiScale.Default);
    }

    private static Control ScaleSelector(MainWindow window) =>
        window.GetVisualDescendants().OfType<ListBox>().Single(list => list.Name == "ScaleSelector");

    private static void AssertReachable(MainWindow window)
    {
        var view = window.GetVisualDescendants().OfType<MainView>().Single();
        var selector = ScaleSelector(window);
        selector.BringIntoView();
        Dispatcher.UIThread.RunJobs();

        // The page scrolls, so the row must fit the scroll area's viewport once scrolled to, not just the window.
        var page = selector.FindAncestorOfType<ScrollViewer>()!;
        var inPage = new Rect(selector.Bounds.Size).TransformToAABB(selector.TransformToVisual(page)!.Value);
        Assert.True(inPage.Top >= -0.5 && inPage.Bottom <= page.Viewport.Height + 0.5, $"The Interface size row is out of view after scrolling to it: {inPage} in a viewport of {page.Viewport}.");
        var box = new Rect(selector.Bounds.Size).TransformToAABB(selector.TransformToVisual(view)!.Value);
        Assert.True(box.Left >= -0.5 && box.Right <= view.Bounds.Width + 0.5, $"The Interface size row is clipped sideways: {box} in {view.Bounds}.");
        Assert.All(selector.GetVisualDescendants().OfType<ListBoxItem>(), item =>
        {
            var itemBox = new Rect(item.Bounds.Size).TransformToAABB(item.TransformToVisual(view)!.Value);
            Assert.True(itemBox.Right <= view.Bounds.Width + 0.5, $"A size is clipped sideways: {itemBox} in {view.Bounds}.");
        });
    }

    // Scrolls the page so the row sits a little below the top, for the picture.
    private static void ScrollRowForRender(MainWindow window)
    {
        var selector = ScaleSelector(window);
        var page = selector.FindAncestorOfType<ScrollViewer>()!;
        var top = new Rect(selector.Bounds.Size).TransformToAABB(selector.TransformToVisual(page)!.Value).Top;
        page.Offset = new Vector(0, page.Offset.Y + top - 120);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task Settings_at_the_largest_size_a_1280_by_800_screen_holds_keeps_the_size_row_in_reach()
    {
        var screen = new MainWindow.ScreenRoom(new PixelRect(0, 0, 1280, 800), 1.0);
        var (window, main) = await OpenSettingsAsync(screen, 200);
        try
        {
            Assert.Equal(175, Theme.UiScalePercent);
            var view = window.GetVisualDescendants().OfType<MainView>().Single();
            Assert.True(view.Bounds.Width >= 640 - 0.5 && view.Bounds.Height >= 420 - 0.5, $"The screens got {view.Bounds.Size}, less than they were built for.");
            AssertReachable(window);
            ScrollRowForRender(window);
            ThemeTestSupport.SaveFrame(window, Path.Combine(OutputFolder, "settings-interface-size-175-on-1280x800.png"));
        }
        finally
        {
            Finish(window, main);
        }
    }

    [AvaloniaTheory]
    [InlineData(125)]
    [InlineData(200)]
    public async Task Choosing_a_larger_size_keeps_the_size_choice_in_view(int percent)
    {
        var screen = new MainWindow.ScreenRoom(new PixelRect(0, 0, 3840, 2160), 1.0);
        var (window, main) = await OpenSettingsAsync(screen, 100);
        try
        {
            // Roomy enough that a larger size does not have to grow the window, so the viewport really shrinks.
            window.Width = 1400;
            window.Height = 900;
            Dispatcher.UIThread.RunJobs();
            var selector = ScaleSelector(window);
            var page = selector.FindAncestorOfType<ScrollViewer>()!;

            // The choice sits at the bottom edge of the page's viewport, as it does after scrolling to it with the keyboard.
            var top = new Rect(selector.Bounds.Size).TransformToAABB(selector.TransformToVisual(page)!.Value).Top;
            page.Offset = new Vector(0, page.Offset.Y + top - (page.Viewport.Height - selector.Bounds.Height - 8));
            Dispatcher.UIThread.RunJobs();
            Assert.True(InView(selector, page), "the setup must start with the size choice in view");

            var appearance = Assert.IsType<SettingsScreenViewModel>(main.Shell!.CurrentScreen).Appearance;
            appearance.SelectedScale = appearance.ScaleOptions.Single(option => option.Percent == percent);
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(100, TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(percent, Theme.UiScalePercent);
            Assert.True(InView(selector, page), $"the size choice scrolled out of view: offset {page.Offset}, viewport {page.Viewport}");
        }
        finally
        {
            Finish(window, main);
        }
    }

    private static bool InView(Control selector, ScrollViewer page)
    {
        var inPage = new Rect(selector.Bounds.Size).TransformToAABB(selector.TransformToVisual(page)!.Value);
        return inPage.Top >= -0.5 && inPage.Bottom <= page.Viewport.Height + 0.5;
    }

    [AvaloniaFact]
    public async Task Settings_on_a_1080p_screen_at_the_systems_200_percent_stays_at_100_percent_with_the_larger_sizes_switched_off()
    {
        var screen = new MainWindow.ScreenRoom(new PixelRect(0, 0, 1920, 1040), 2.0);
        var (window, main) = await OpenSettingsAsync(screen, 200);
        try
        {
            Assert.Equal(100, Theme.UiScalePercent);
            AssertReachable(window);
            var screenModel = Assert.IsType<SettingsScreenViewModel>(main.Shell!.CurrentScreen);
            var options = screenModel.Appearance.ScaleOptions;
            Assert.Equal(100, screenModel.Appearance.SelectedScale!.Percent);
            Assert.All(options.Where(option => option.Percent > 100), option => Assert.False(option.IsAvailable));
            var switchedOff = ScaleSelector(window).GetVisualDescendants().OfType<ListBoxItem>().Where(item => !item.IsEnabled).ToList();
            Assert.Equal(4, switchedOff.Count);
            Assert.All(switchedOff, item => Assert.Equal("Your screen is too small for this size.", ToolTip.GetTip(item)));
            Assert.Equal(
                ["100%", "125%. Your screen is too small for this size.", "150%. Your screen is too small for this size.", "175%. Your screen is too small for this size.", "200%. Your screen is too small for this size."],
                ScaleSelector(window).GetVisualDescendants().OfType<ListBoxItem>().Select(Avalonia.Automation.AutomationProperties.GetName));
            Assert.Equal(
                "Not available on this screen: 125%, 150%, 175%, 200%. Your screen is too small for this size.",
                Avalonia.Automation.AutomationProperties.GetHelpText(ScaleSelector(window)));
            ScrollRowForRender(window);
            ThemeTestSupport.SaveFrame(window, Path.Combine(OutputFolder, "settings-interface-size-limited-on-1080p-at-200.png"));
        }
        finally
        {
            Finish(window, main);
        }
    }
}
