using Savepoint.App.Features.Shell;

namespace Savepoint.App.Tests.Features.Shell;

// A screen for a destination a test does not exercise: the shell wants one factory per destination, and the app registers all of them.
internal sealed class StandInScreen(NavDestination destination) : IScreen
{
    public NavDestination Destination { get; } = destination;

    public void OnShown()
    {
    }
}

internal sealed class StandInScreenFactory(NavDestination destination) : IScreenFactory
{
    public NavDestination Destination { get; } = destination;

    public IScreen Create(IShellContext shell) => new StandInScreen(Destination);
}

internal static class StandInScreens
{
    // The given factories, plus a stand-in for every destination none of them builds.
    public static IReadOnlyList<IScreenFactory> Complete(IEnumerable<IScreenFactory> real)
    {
        var factories = real.ToList();
        foreach (var destination in Enum.GetValues<NavDestination>().Where(destination => factories.All(factory => factory.Destination != destination)))
        {
            factories.Add(new StandInScreenFactory(destination));
        }

        return factories;
    }
}
