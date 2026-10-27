using Savepoint.App.Features.Settings;
using Savepoint.App.Features.Shell;
using Savepoint.App.Platform;

namespace Savepoint.App.Composition;

public static partial class FeatureComposition
{
    /// <summary>The Settings screen's factory.</summary>
    /// <param name="context">Shared App services.</param>
    /// <param name="environment">What Settings asks of the machine; the running app's when null.</param>
    public static IScreenFactory SettingsScreen(FeatureContext context, SettingsEnvironment? environment = null) =>
        new SettingsScreenFactory(SettingsDependenciesOf(context, environment));

    /// <summary>What the Settings screen is built from.</summary>
    /// <param name="context">Shared App services.</param>
    /// <param name="environment">What Settings asks of the machine; the running app's when null.</param>
    public static SettingsDependencies SettingsDependenciesOf(FeatureContext context, SettingsEnvironment? environment = null) =>
        new(
            context.Services.Settings,
            context.SettingsWriter,
            context.Services.Jobs,
            context.Dialogs,
            context.Text,
            context.Toasts,
            context.Dispatcher,
            environment ?? SettingsEnvironmentOf(context.Services, context.Clock));

    // The running app's file manager and log folder, so the About section opens a folder the way the rest of the app reveals files.
    // The apps menu entry is rebuilt on the features' clock so a test that moves time reaches it.
    internal static SettingsEnvironment SettingsEnvironmentOf(AppServices services, TimeProvider? clock = null) =>
        SettingsEnvironment.ForThisApp(services.FileRevealer, services.LogFolder) with { AppsMenu = AppsMenuEntry.ForThisApp(clock) };
}
