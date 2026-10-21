using Savepoint.App.Composition;
using Savepoint.App.Features.Shell;

namespace Savepoint.App.Features.Settings;

/// <summary>Builds the Settings screen for the shell.</summary>
public sealed class SettingsScreenFactory : IScreenFactory
{
    private readonly FeatureContext _context;
    private readonly SettingsEnvironment _environment;

    /// <param name="context">The shared App services.</param>
    /// <param name="environment">The notices file, the file manager and the version.</param>
    public SettingsScreenFactory(FeatureContext context, SettingsEnvironment environment)
    {
        _context = context;
        _environment = environment;
    }

    /// <inheritdoc />
    public NavDestination Destination => NavDestination.Settings;

    /// <inheritdoc />
    public IScreen Create(IShellContext shell)
    {
        var services = _context.Services;
        return new SettingsScreenViewModel(
            shell,
            new SettingsDependencies(services.Settings, services.Jobs, _context.Dialogs, _context.Text, _context.Toasts, _context.Dispatcher, _environment));
    }
}
