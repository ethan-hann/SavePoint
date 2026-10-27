using Avalonia.Platform.Storage;
using Savepoint.App.Composition;
using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Features.Library;
using Savepoint.App.Features.Settings;
using Savepoint.App.Features.Shell;
using Savepoint.App.Platform;

namespace Savepoint.App;

/// <summary>
/// The composition root of the App: the one place that wires view models to the Api. A screen is added by listing its
/// factory in <see cref="ScreenFactories"/>; nothing else in the shell needs to know about it.
/// </summary>
public static class AppComposition
{
    /// <summary>Builds the main window's view model: the startup checks first, then the shell once they pass.</summary>
    /// <param name="services">The Api to build from.</param>
    /// <param name="text">Formats every message.</param>
    /// <param name="theme">Applies the saved theme once settings load.</param>
    /// <param name="storage">The storage provider of the window pickers attach to; null while no window exists.</param>
    /// <param name="dispatcher">Brings Api events to the UI thread; the real one when null.</param>
    /// <param name="clock">Times toasts; the system clock when null.</param>
    /// <param name="dialogs">Replaces the dialogs and pickers, for tests; the real ones over the window's dialog overlay when null.</param>
    /// <param name="settingsEnvironment">Replaces what Settings reads from the machine, for tests; the running app's when null.</param>
    /// <param name="exitWait">How long closing waits for cancelled jobs to end, for tests; the exit guard's default when null.</param>
    /// <param name="attention">Calls the player back when a session ends behind another window; does nothing when null.</param>
    public static MainWindowViewModel Create(
        AppServices services,
        UiFormatter text,
        IThemeApplier theme,
        Func<IStorageProvider?> storage,
        IUiDispatcher? dispatcher = null,
        TimeProvider? clock = null,
        IDialogs? dialogs = null,
        SettingsEnvironment? settingsEnvironment = null,
        TimeSpan? exitWait = null,
        ISessionAttention? attention = null)
    {
        var ui = dispatcher ?? new AvaloniaUiDispatcher();
        var toasts = new ToastHostViewModel(clock ?? TimeProvider.System, ui);
        var dialogStack = new DialogHostViewModel();
        var pickers = dialogs ?? new AvaloniaDialogs(dialogStack, text, storage);
        var startup = new StartupViewModel(services.AppStatus, text, ui, services.BackendStarted, services.LogFolder, services.FileRevealer, pickers);

        var libraryRuns = new LibraryRunTracker();

        ShellViewModel CreateShell()
        {
            var feature = new FeatureContext(services, pickers, text, toasts, ui, clock, attention);
            return new(new ShellDependencies(
                services.Games,
                services.Profiles,
                services.Settings,
                feature.SettingsWriter,
                services.Jobs,
                text,
                ui,
                pickers,
                toasts,
                theme,
                ScreenFactories(feature, settingsEnvironment),
                FeatureComposition.Artwork(feature),
                FeatureComposition.LaunchFlow(feature),
                libraryRuns,
                feature.Clock));
        }

        return new MainWindowViewModel(startup, CreateShell, dialogStack, toasts, new ExitGuard(services.Jobs, pickers, text, exitWait, libraryRuns));
    }

    /// <summary>One factory per screen, each supplied by its feature's <see cref="FeatureComposition"/> file.</summary>
    private static IReadOnlyList<IScreenFactory> ScreenFactories(FeatureContext feature, SettingsEnvironment? settingsEnvironment)
    {
        var editor = FeatureComposition.GameEditor(feature);
        return
        [
            FeatureComposition.GamesScreen(feature, editor),
            FeatureComposition.HistoryScreen(feature),
            FeatureComposition.ActivityScreen(feature),
            FeatureComposition.SettingsScreen(feature, settingsEnvironment),
        ];
    }
}
