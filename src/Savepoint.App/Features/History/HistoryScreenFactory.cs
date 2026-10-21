using Savepoint.App.Composition;
using Savepoint.App.Features.Shell;

namespace Savepoint.App.Features.History;

/// <summary>Builds the History screen for the shell.</summary>
public sealed class HistoryScreenFactory : IScreenFactory
{
    private readonly FeatureContext _context;
    private readonly TimeProvider _clock;

    /// <param name="context">The shared App services.</param>
    /// <param name="clock">What "this week" is measured from.</param>
    public HistoryScreenFactory(FeatureContext context, TimeProvider clock)
    {
        _context = context;
        _clock = clock;
    }

    /// <inheritdoc />
    public NavDestination Destination => NavDestination.History;

    /// <inheritdoc />
    public IScreen Create(IShellContext shell)
    {
        var services = _context.Services;
        return new HistoryScreenViewModel(
            shell,
            new HistoryDependencies(services.History, services.Profiles, services.Games, services.Jobs, _context.Text, _context.Dispatcher, _clock));
    }
}
