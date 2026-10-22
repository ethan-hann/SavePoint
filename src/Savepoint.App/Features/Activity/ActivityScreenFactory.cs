using Savepoint.App.Features.Shell;

namespace Savepoint.App.Features.Activity;

/// <summary>Builds the Activity screen for the shell.</summary>
public sealed class ActivityScreenFactory : IScreenFactory
{
    private readonly ActivityDependencies _dependencies;

    /// <param name="dependencies">What the screen is built from.</param>
    public ActivityScreenFactory(ActivityDependencies dependencies)
    {
        _dependencies = dependencies;
    }

    /// <inheritdoc />
    public NavDestination Destination => NavDestination.Activity;

    /// <inheritdoc />
    public IScreen Create(IShellContext shell) => new ActivityScreenViewModel(shell, _dependencies);
}
