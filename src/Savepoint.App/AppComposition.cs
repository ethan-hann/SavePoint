using Avalonia.Platform.Storage;
using Savepoint.App.Composition;
using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Features.Settings;
using Savepoint.App.Features.Shell;

namespace Savepoint.App;

/// <summary>
/// The composition root of the App: the one place that wires view models to the Api. A screen is added by listing its
/// factory in <see cref="ScreenFactories"/>; nothing else in the shell needs to know about it.
/// </summary>
public static class AppComposition
{
    /// <summary>
    /// Builds the main window's view model. With no <paramref name="services"/> the startup screen says services are
    /// not available and the shell never appears.
    /// </summary>
    /// <param name="services">The Api to build from; null while no real services exist.</param>
    /// <param name="text">Formats every message.</param>
    /// <param name="theme">Applies the saved theme once settings load.</param>
    /// <param name="storage">The storage provider of the window pickers attach to; null while no window exists.</param>
    /// <param name="dispatcher">Brings Api events to the UI thread; the real one when null.</param>
    /// <param name="clock">Times toasts; the system clock when null.</param>
    /// <param name="dialogs">Replaces the dialogs and pickers, for tests; the real ones over the window's dialog overlay when null.</param>
    /// <param name="settingsEnvironment">Replaces what Settings reads from the machine, for tests; the running app's when null.</param>
    public static MainWindowViewModel Create(
        AppServices? services,
        UiFormatter text,
        IThemeApplier theme,
        Func<IStorageProvider?> storage,
        IUiDispatcher? dispatcher = null,
        TimeProvider? clock = null,
        IDialogs? dialogs = null,
        SettingsEnvironment? settingsEnvironment = null)
    {
        var ui = dispatcher ?? new AvaloniaUiDispatcher();
        var toasts = new ToastHostViewModel(clock ?? TimeProvider.System, ui);
        var dialogStack = new DialogHostViewModel();
        var pickers = dialogs ?? new AvaloniaDialogs(dialogStack, text, storage);
        var startup = new StartupViewModel(services?.AppStatus, text, services?.BackendStarted ?? true, services?.LogFolder, services?.FileRevealer, pickers);

        ShellViewModel CreateShell()
        {
            var api = services!;
            var feature = new FeatureContext(api, pickers, text, toasts, ui, clock);
            return new(new ShellDependencies(
                api.Games,
                api.Profiles,
                api.Settings,
                feature.SettingsWriter,
                api.Jobs,
                text,
                ui,
                pickers,
                toasts,
                theme,
                ScreenFactories(feature, settingsEnvironment),
                LaunchFlow: FeatureComposition.LaunchFlow(feature),
                Artwork: FeatureComposition.Artwork(feature)));
        }

        return new MainWindowViewModel(startup, CreateShell, dialogStack, toasts, new ExitGuard(services?.Jobs, pickers, text));
    }

    /// <summary>
    /// One factory per screen, each supplied by its feature's <see cref="FeatureComposition"/> file; a destination without
    /// a factory shows the placeholder.
    /// </summary>
    private static IReadOnlyList<IScreenFactory> ScreenFactories(FeatureContext feature, SettingsEnvironment? settingsEnvironment)
    {
        var editor = FeatureComposition.GameEditor(feature);
        return new[]
        {
            FeatureComposition.GamesScreen(feature, editor),
            FeatureComposition.HistoryScreen(feature, editor),
            FeatureComposition.SettingsScreen(feature, editor, settingsEnvironment),
        }.OfType<IScreenFactory>().ToList();
    }
}
