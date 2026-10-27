using Savepoint.App.Features.Shell;

namespace Savepoint.App.Features.History;

/// <summary>Builds the History screen for the shell.</summary>
public sealed class HistoryScreenFactory : IScreenFactory
{
    private readonly HistoryDependencies _dependencies;

    /// <param name="dependencies">What the screen is built from.</param>
    public HistoryScreenFactory(HistoryDependencies dependencies)
    {
        _dependencies = dependencies;
    }

    /// <inheritdoc />
    public NavDestination Destination => NavDestination.History;

    /// <inheritdoc />
    public IScreen Create(IShellContext shell) => new HistoryScreenViewModel(shell, _dependencies);
}
