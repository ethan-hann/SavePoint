namespace Savepoint.App.Features.Shell;

/// <summary>What a destination shows until a real screen is registered for it.</summary>
public sealed class PlaceholderScreenViewModel : IScreen
{
    /// <param name="destination">The destination it stands in for.</param>
    public PlaceholderScreenViewModel(NavDestination destination)
    {
        Destination = destination;
    }

    /// <inheritdoc />
    public NavDestination Destination { get; }

    /// <inheritdoc />
    public void OnShown()
    {
    }
}
