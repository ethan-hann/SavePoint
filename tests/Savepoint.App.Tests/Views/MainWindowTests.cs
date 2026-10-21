using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Shell;
using Savepoint.App.Localization;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Views;

namespace Savepoint.App.Tests.Views;

public class MainWindowTests
{
    [AvaloniaFact]
    public void The_window_hosts_the_main_view()
    {
        var window = new MainWindow();

        Assert.IsType<MainView>(window.Content);
    }

    [AvaloniaFact]
    public void Title_comes_from_the_catalog()
    {
        var window = new MainWindow();

        Assert.Equal("SavePoint", window.Title);
    }

    [AvaloniaFact]
    public void The_window_carries_the_app_icon()
    {
        var window = new MainWindow();

        Assert.NotNull(window.Icon);
    }

    [AvaloniaFact]
    public void Window_has_the_default_and_minimum_sizes()
    {
        var window = new MainWindow();

        Assert.Equal(1040, window.Width);
        Assert.Equal(660, window.Height);
        Assert.Equal(760, window.MinWidth);
        Assert.Equal(520, window.MinHeight);
    }

    [AvaloniaFact]
    public async Task With_no_services_connected_the_window_says_it_is_not_ready()
    {
        var text = new UiFormatter(LocalizationCatalog.Current);
        var main = AppComposition.Create(
            null,
            text,
            new ThemeServiceApplier(((App)App.Current!).Theme),
            () => null,
            new ImmediateUiDispatcher(),
            new ManualTimeProvider(),
            new FakeDialogs());
        var window = new MainWindow { DataContext = main };
        window.Show();

        await main.InitializeAsync();
        Dispatcher.UIThread.RunJobs();

        var titles = window.GetVisualDescendants().OfType<TextBlock>().Where(block => block.IsEffectivelyVisible).Select(block => block.Text).ToList();
        Assert.Contains("SavePoint isn't ready yet", titles);
        Assert.Null(main.Shell);
    }
}
