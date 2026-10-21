using Savepoint.App.Features.Shell;

namespace Savepoint.App.Features.Games;

/// <summary>Builds the Games screen for the shell.</summary>
public sealed class GamesScreenFactory : IScreenFactory
{
    private readonly GamesDependencies _dependencies;

    /// <param name="dependencies">What the screen is built from.</param>
    public GamesScreenFactory(GamesDependencies dependencies)
    {
        _dependencies = dependencies;
    }

    /// <inheritdoc />
    public NavDestination Destination => NavDestination.Games;

    /// <inheritdoc />
    public IScreen Create(IShellContext shell) => new GamesScreenViewModel(shell, _dependencies);
}
