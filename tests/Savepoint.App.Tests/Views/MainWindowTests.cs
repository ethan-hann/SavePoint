using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Shell;
using Savepoint.App.Localization;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Theme;
using Savepoint.App.Views;
using Savepoint.Core.Api;

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
        Assert.Equal(640, window.MinWidth);
        Assert.Equal(420, window.MinHeight);
    }

    [AvaloniaFact]
    public void The_minimum_size_fits_the_work_area_of_the_scaled_screens()
    {
        var window = new MainWindow();

        foreach (var (name, width, height) in LayoutAudit.ScaledScreens)
        {
            Assert.True(window.MinWidth <= width, $"A {window.MinWidth} px minimum width does not fit {name} ({width} px).");
            Assert.True(window.MinHeight <= height, $"A {window.MinHeight} px minimum height does not fit {name} ({height} px).");
        }
    }

    [AvaloniaFact]
    public async Task Closing_while_a_storage_move_runs_asks_first_and_staying_keeps_the_window_open()
    {
        var (window, backend, dialogs) = await WindowWithRunningMoveAsync(answer: false);

        window.Close();
        await SettleAsync(() => dialogs.Confirmations.Count == 1);

        Assert.True(window.IsVisible);
        Assert.Empty(backend.Calls.To<IJobsApi>(nameof(IJobsApi.CancelAsync)));
    }

    [AvaloniaFact]
    public async Task Quitting_anyway_during_a_storage_move_cancels_it_and_closes_the_window()
    {
        var (window, backend, dialogs) = await WindowWithRunningMoveAsync(answer: true);

        window.Close();
        await SettleAsync(() => !window.IsVisible);

        Assert.False(window.IsVisible);
        Assert.Single(dialogs.Confirmations);
        Assert.Single(backend.Calls.To<IJobsApi>(nameof(IJobsApi.CancelAsync)));
    }

    private static async Task<(MainWindow Window, FakeBackend Backend, FakeDialogs Dialogs)> WindowWithRunningMoveAsync(bool answer)
    {
        var backend = FakeBackend.Mockup();
        var dialogs = new FakeDialogs().AnswerConfirm(answer);
        var services = new AppServices(backend.Games, backend.Profiles, backend.Launch, backend.Settings, backend.History, backend.Jobs, backend.AppStatus);
        var main = AppComposition.Create(
            services,
            new UiFormatter(LocalizationCatalog.Current),
            new ThemeServiceApplier(((App)App.Current!).Theme),
            () => null,
            new ImmediateUiDispatcher(),
            new ManualTimeProvider(),
            dialogs);
        var window = new MainWindow { DataContext = main };
        window.Show();
        FakeAssert.Ok(await backend.Settings.RelocateStorageAsync(@"E:\SavePoint"));
        return (window, backend, dialogs);
    }

    // The close runs its question and its waits on the UI thread; pump it until the outcome shows or a generous limit passes.
    private static async Task SettleAsync(Func<bool> done)
    {
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!done() && DateTime.UtcNow < until)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Dispatcher.UIThread.RunJobs();
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
