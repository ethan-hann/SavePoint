using Avalonia.Platform.Storage;
using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Common.Toasts;
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
    public static MainWindowViewModel Create(
        AppServices? services,
        UiFormatter text,
        IThemeApplier theme,
        Func<IStorageProvider?> storage,
        IUiDispatcher? dispatcher = null,
        TimeProvider? clock = null,
        IDialogs? dialogs = null)
    {
        var ui = dispatcher ?? new AvaloniaUiDispatcher();
        var toasts = new ToastHostViewModel(clock ?? TimeProvider.System, ui);
        var dialogStack = new DialogHostViewModel();
        var pickers = dialogs ?? new AvaloniaDialogs(dialogStack, text, storage);
        var startup = new StartupViewModel(services?.AppStatus, text);

        ShellViewModel CreateShell() => new(new ShellDependencies(
            services!.Games,
            services.Profiles,
            services.Settings,
            services.Jobs,
            text,
            ui,
            pickers,
            toasts,
            theme,
            ScreenFactories(services, pickers, text, toasts),
            LaunchFlow: null));

        return new MainWindowViewModel(startup, CreateShell, dialogStack, toasts);
    }

    /// <summary>
    /// One factory per screen. Games, History, Settings and the launch flow register here as they are built; a
    /// destination without a factory shows the placeholder.
    /// </summary>
    private static IReadOnlyList<IScreenFactory> ScreenFactories(AppServices services, IDialogs dialogs, UiFormatter text, ToastHostViewModel toasts)
    {
        _ = (services, dialogs, text, toasts);
        return [];
    }
}
