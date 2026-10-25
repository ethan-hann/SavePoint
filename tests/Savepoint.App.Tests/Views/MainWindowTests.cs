using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
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

    [AvaloniaFact]
    public async Task An_application_shutdown_during_a_storage_move_asks_first_and_staying_cancels_the_quit()
    {
        var (window, backend, dialogs) = await WindowWithRunningMoveAsync(answer: false);
        var closing = ClosingArgs(WindowCloseReason.ApplicationShutdown);

        window.RaiseClosing(closing);
        await SettleAsync(() => dialogs.Confirmations.Count == 1);

        Assert.True(closing.Cancel);
        Assert.True(window.IsVisible);
        Assert.Empty(backend.Calls.To<IJobsApi>(nameof(IJobsApi.CancelAsync)));
    }

    [AvaloniaFact]
    public async Task Quitting_anyway_after_an_application_shutdown_cancels_the_move_and_closes_the_window()
    {
        var (window, backend, dialogs) = await WindowWithRunningMoveAsync(answer: true);

        window.RaiseClosing(ClosingArgs(WindowCloseReason.ApplicationShutdown));
        await SettleAsync(() => !window.IsVisible);

        Assert.False(window.IsVisible);
        Assert.Single(dialogs.Confirmations);
        Assert.Single(backend.Calls.To<IJobsApi>(nameof(IJobsApi.CancelAsync)));
    }

    [AvaloniaFact]
    public async Task With_nothing_running_the_window_closes_after_the_closing_event_and_not_inside_it()
    {
        var (window, _, dialogs) = await WindowWithRunningMoveAsync(answer: true, startMove: false);
        var closing = ClosingArgs(WindowCloseReason.ApplicationShutdown);

        window.RaiseClosing(closing);

        Assert.True(closing.Cancel);
        Assert.True(window.IsVisible);
        await SettleAsync(() => !window.IsVisible);
        Assert.False(window.IsVisible);
        Assert.Empty(dialogs.Confirmations);
    }

    [AvaloniaFact]
    public async Task A_system_shutdown_is_never_held_up_by_a_question()
    {
        var (window, _, dialogs) = await WindowWithRunningMoveAsync(answer: false);
        var closing = ClosingArgs(WindowCloseReason.OSShutdown);

        window.RaiseClosing(closing);
        Dispatcher.UIThread.RunJobs();

        Assert.False(closing.Cancel);
        Assert.Empty(dialogs.Confirmations);
    }

    [AvaloniaFact]
    public async Task Closing_while_a_snapshot_is_taken_asks_before_anything_is_stopped()
    {
        var (window, backend, dialogs) = await WindowWithRunningSnapshotAsync();

        window.Close();
        await SettleAsync(() => dialogs.Shown.Count == 1);

        Assert.IsType<CloseDuringSnapshotDialogViewModel>(Assert.Single(dialogs.Shown));
        Assert.True(window.IsVisible);
        Assert.Empty(backend.Calls.To<IJobsApi>(nameof(IJobsApi.CancelAsync)));
    }

    [AvaloniaFact]
    public async Task Minimizing_from_the_question_minimizes_the_window_and_the_snapshot_keeps_running()
    {
        var (window, backend, dialogs) = await WindowWithRunningSnapshotAsync();

        window.Close();
        await SettleAsync(() => dialogs.Shown.Count == 1);
        ((CloseDuringSnapshotDialogViewModel)dialogs.Shown[0]).MinimizeCommand.Execute(null);
        await SettleAsync(() => window.WindowState == WindowState.Minimized);

        Assert.Equal(WindowState.Minimized, window.WindowState);
        Assert.True(window.IsVisible);
        Assert.Empty(backend.Calls.To<IJobsApi>(nameof(IJobsApi.CancelAsync)));
    }

    [AvaloniaFact]
    public async Task A_question_asked_while_the_window_is_minimized_restores_it_and_minimizing_again_puts_it_back()
    {
        var (window, _, dialogs) = await WindowWithRunningSnapshotAsync();
        window.WindowState = WindowState.Minimized;

        window.Close();
        await SettleAsync(() => dialogs.Shown.Count == 1);
        var whileAsking = window.WindowState;
        ((CloseDuringSnapshotDialogViewModel)dialogs.Shown[0]).MinimizeCommand.Execute(null);
        await SettleAsync(() => window.WindowState == WindowState.Minimized);

        Assert.Equal(WindowState.Normal, whileAsking);
        Assert.Equal(WindowState.Minimized, window.WindowState);
    }

    [AvaloniaFact]
    public async Task A_minimized_window_with_nothing_running_closes_without_being_restored()
    {
        var (window, _, dialogs) = await WindowWithRunningMoveAsync(answer: true, startMove: false);
        window.WindowState = WindowState.Minimized;
        var states = new List<WindowState>();
        window.PropertyChanged += (_, e) =>
        {
            if (e.Property == Window.WindowStateProperty)
            {
                states.Add(window.WindowState);
            }
        };

        window.Close();
        await SettleAsync(() => !window.IsVisible);

        Assert.False(window.IsVisible);
        Assert.Empty(dialogs.Shown);
        Assert.DoesNotContain(WindowState.Normal, states);
    }

    [AvaloniaFact]
    public async Task Closing_and_stopping_from_the_question_cancels_the_snapshot_and_closes_the_window()
    {
        var (window, backend, dialogs) = await WindowWithRunningSnapshotAsync();

        window.Close();
        await SettleAsync(() => dialogs.Shown.Count == 1);
        ((CloseDuringSnapshotDialogViewModel)dialogs.Shown[0]).CloseAndStopCommand.Execute(null);
        await SettleAsync(() => !window.IsVisible);

        Assert.False(window.IsVisible);
        Assert.Single(backend.Calls.To<IJobsApi>(nameof(IJobsApi.CancelAsync)));
    }

    [AvaloniaFact]
    public async Task Escape_on_the_snapshot_question_returns_to_the_app_and_a_second_close_asks_again()
    {
        var (window, backend, dialogs) = await WindowWithRunningSnapshotAsync();

        window.Close();
        await SettleAsync(() => dialogs.Shown.Count == 1);
        dialogs.Shown[0].RequestDismiss();
        await SettleAsync(() => false, TimeSpan.FromMilliseconds(200));
        window.Close();
        await SettleAsync(() => dialogs.Shown.Count == 2);

        Assert.True(window.IsVisible);
        Assert.Equal(WindowState.Normal, window.WindowState);
        Assert.Equal(2, dialogs.Shown.Count);
        Assert.Empty(backend.Calls.To<IJobsApi>(nameof(IJobsApi.CancelAsync)));
    }

    [AvaloniaFact]
    public async Task A_system_shutdown_during_a_snapshot_is_not_held_up_by_the_question()
    {
        var (window, _, dialogs) = await WindowWithRunningSnapshotAsync();
        var closing = ClosingArgs(WindowCloseReason.OSShutdown);

        window.RaiseClosing(closing);
        Dispatcher.UIThread.RunJobs();

        Assert.False(closing.Cancel);
        Assert.Empty(dialogs.Shown);
        Assert.Empty(dialogs.Confirmations);
    }

    [AvaloniaFact]
    public async Task With_no_snapshot_running_the_window_closes_without_the_snapshot_question()
    {
        var (window, _, dialogs) = await WindowWithRunningMoveAsync(answer: true, startMove: false);

        window.Close();
        await SettleAsync(() => !window.IsVisible);

        Assert.False(window.IsVisible);
        Assert.Empty(dialogs.Shown);
    }

    // The arguments the window gets when the application shuts down; Avalonia builds them itself, so the test has to as well.
    private static WindowClosingEventArgs ClosingArgs(WindowCloseReason reason)
    {
        var constructor = typeof(WindowClosingEventArgs).GetConstructors(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic).Single();
        var arguments = constructor.GetParameters().Select(parameter => parameter.ParameterType == typeof(WindowCloseReason) ? reason : (object)false).ToArray();
        return (WindowClosingEventArgs)constructor.Invoke(arguments);
    }

    // Raises the close a quit sends, which only the application can send.
    private sealed class QuittableWindow : MainWindow
    {
        public void RaiseClosing(WindowClosingEventArgs e) => OnClosing(e);
    }

    private static async Task<(QuittableWindow Window, FakeBackend Backend, FakeDialogs Dialogs)> WindowWithRunningSnapshotAsync()
    {
        var (window, backend, dialogs) = await WindowWithRunningMoveAsync(answer: true, startMove: false);
        FakeAssert.Ok(await backend.Profiles.CaptureAsync(SampleScenario.SkyrimId, SampleScenario.ModdedId, "A point"));
        return (window, backend, dialogs);
    }

    private static async Task<(QuittableWindow Window, FakeBackend Backend, FakeDialogs Dialogs)> WindowWithRunningMoveAsync(bool answer, bool startMove = true)
    {
        var backend = FakeBackend.Sample();
        var dialogs = new FakeDialogs().AnswerConfirm(answer);
        var services = new AppServices(backend.Games, backend.Profiles, backend.Launch, backend.Settings, backend.History, backend.Jobs, backend.AppStatus, backend.Artwork);
        var main = AppComposition.Create(
            services,
            new UiFormatter(LocalizationCatalog.Current),
            new ThemeServiceApplier(((App)App.Current!).Theme),
            () => null,
            new ImmediateUiDispatcher(),
            new ManualTimeProvider(),
            dialogs,
            exitWait: TimeSpan.FromMilliseconds(100));
        var window = new QuittableWindow { DataContext = main };
        window.Show();
        if (startMove)
        {
            FakeAssert.Ok(await backend.Settings.RelocateStorageAsync(@"E:\SavePoint"));
        }

        return (window, backend, dialogs);
    }

    // The close runs its question and its waits on the UI thread; pump it until the outcome shows or a generous limit passes.
    private static async Task SettleAsync(Func<bool> done, TimeSpan? limit = null)
    {
        var until = DateTime.UtcNow + (limit ?? TimeSpan.FromSeconds(10));
        while (!done() && DateTime.UtcNow < until)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Dispatcher.UIThread.RunJobs();
    }
}
