using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Shell;
using Savepoint.App.Hosting;
using Savepoint.App.Localization;
using Savepoint.App.Platform;
using Savepoint.App.Theme;
using Savepoint.App.Views;
using Savepoint.Core.Catalog;

namespace Savepoint.App;

public partial class App : Application
{
    /// <summary>
    /// Owns the application's color resources. Call <see cref="ThemeService.Apply"/> from the UI thread when the saved
    /// theme or accent changes, and set <see cref="ThemeService.ReduceMotion"/> from the system accessibility setting.
    /// </summary>
    public ThemeService Theme { get; private set; } = null!;

    /// <summary>
    /// The Api the app runs on. <c>Main</c> sets it from the real backend before the window opens; a test sets it to run
    /// against anything else. Null makes the startup screen say services are not available.
    /// </summary>
    public static AppServices? Services { get; set; }

    /// <summary>The guard that keeps one copy of the app per user; its activation requests bring the window forward. Null in tests.</summary>
    public static SingleInstance? SingleInstance { get; set; }

    /// <summary>
    /// True when the app should follow the operating system: notifications, reduced motion and activation by a second copy.
    /// <c>Main</c> turns it on; tests and the designer leave it off, so they never reach into the system.
    /// </summary>
    public static bool IntegrateWithSystem { get; set; }

    /// <summary>True when this copy only shows the brief "already running" notice and then ends.</summary>
    public static bool ShowAlreadyRunningNotice { get; set; }

    public override void Initialize()
    {
        // Must be set before any XAML that uses the Loc extension is loaded.
        LocalizationCatalog.Current = LocalizationCatalog.LoadEmbedded("en");
        AvaloniaXamlLoader.Load(this);

        // Settings are not read yet, so this starts from the catalog defaults; the shell re-applies the saved choice.
        Theme = ThemeService.CreateForApplication(this);
        Theme.Apply(ThemePreference.System, AccentColorId.Blue);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var text = new UiFormatter(LocalizationCatalog.Current);
            if (ShowAlreadyRunningNotice)
            {
                desktop.MainWindow = new AlreadyRunningWindow(text);
                base.OnFrameworkInitializationCompleted();
                return;
            }

            var window = new MainWindow();
            var main = AppComposition.Create(
                Services,
                text,
                new ThemeServiceApplier(Theme),
                () => window.StorageProvider);
            window.DataContext = main;
            window.Opened += (_, _) => _ = main.InitializeAsync();
            desktop.MainWindow = window;

            if (IntegrateWithSystem && Services is not null)
            {
                var integration = HostIntegration.Attach(window, Theme, Services, text, SingleInstance);
                desktop.Exit += (_, _) => integration.Dispose();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
