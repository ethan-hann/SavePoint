using Savepoint.App.Features.Shell;

namespace Savepoint.App.Features.Settings;

/// <summary>Builds the Settings screen for the shell.</summary>
public sealed class SettingsScreenFactory : IScreenFactory
{
    private readonly SettingsDependencies _dependencies;

    /// <param name="dependencies">What the screen is built from.</param>
    public SettingsScreenFactory(SettingsDependencies dependencies)
    {
        _dependencies = dependencies;
    }

    /// <inheritdoc />
    public NavDestination Destination => NavDestination.Settings;

    /// <inheritdoc />
    public IScreen Create(IShellContext shell) => new SettingsScreenViewModel(shell, _dependencies);
}
