using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Shell;
using Savepoint.App.Localization;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Views;

namespace Savepoint.App.Tests.Views;

// The scale reaches the dialogs the window shows, whether they open before or after it changes.
public class MainWindowUiScaleTests
{
    private static MainWindow ShownWindow(out MainWindowViewModel main)
    {
        var backend = FakeBackend.Sample();
        main = AppComposition.Create(
            new AppServices(backend.Games, backend.Profiles, backend.Launch, backend.Settings, backend.History, backend.Jobs, backend.AppStatus, backend.Artwork),
            new UiFormatter(LocalizationCatalog.Current),
            new ThemeServiceApplier(((App)Application.Current!).Theme),
            () => null,
            new ImmediateUiDispatcher(),
            new ManualTimeProvider());
        var window = new MainWindow { DataContext = main };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static Task<bool> AskAsync(MainWindowViewModel main) =>
        main.Dialogs.ShowAsync(new ConfirmDialogViewModel(new ConfirmRequest("Remove it?", "It goes away.", "Remove"), "Cancel"));

    private static double ScaleOfDialog(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        var dialog = window.GetVisualDescendants().OfType<ConfirmDialogView>().Single();
        return dialog.TransformToVisual(window)!.Value.M11;
    }

    [AvaloniaFact]
    public void A_dialog_opened_after_the_scale_changed_is_drawn_at_that_scale()
    {
        var window = ShownWindow(out var main);
        var app = (App)Application.Current!;
        try
        {
            app.Theme.SetUiScale(150);
            Dispatcher.UIThread.RunJobs();

            _ = AskAsync(main);

            Assert.Equal(1.5, ScaleOfDialog(window), 3);
        }
        finally
        {
            app.Theme.SetUiScale(100);
            window.Close();
        }
    }

    [AvaloniaFact]
    public void A_dialog_that_is_open_follows_a_change_of_scale()
    {
        var window = ShownWindow(out var main);
        var app = (App)Application.Current!;
        try
        {
            _ = AskAsync(main);
            Assert.Equal(1.0, ScaleOfDialog(window), 3);

            app.Theme.SetUiScale(200);

            Assert.Equal(2.0, ScaleOfDialog(window), 3);
        }
        finally
        {
            app.Theme.SetUiScale(100);
            window.Close();
        }
    }
}
