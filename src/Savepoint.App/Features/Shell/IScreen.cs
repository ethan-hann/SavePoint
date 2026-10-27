namespace Savepoint.App.Features.Shell;

/// <summary>
/// The view model of one top-level screen. The shell keeps one instance per destination for the whole session, so a
/// screen's state survives navigation and layout changes. Its view is found by <see cref="Common.ViewLocator"/>:
/// <c>GamesScreenViewModel</c> is shown by <c>GamesScreenView</c>.
/// </summary>
public interface IScreen
{
    /// <summary>Which destination this screen fills.</summary>
    NavDestination Destination { get; }

    /// <summary>Called each time the shell switches to this screen, so it can refresh what it shows. Starts its own async work; the shell does not wait.</summary>
    void OnShown();

    /// <summary>Called each time the shell switches away from this screen, so it can stop work that only matters while it is on show. Does nothing by default.</summary>
    void OnHidden()
    {
    }
}

/// <summary>
/// Creates a screen for the shell. Register one per destination in <c>AppComposition</c>; the shell refuses to start
/// when a destination has none.
/// </summary>
public interface IScreenFactory
{
    /// <summary>The destination the factory builds the screen for.</summary>
    NavDestination Destination { get; }

    /// <summary>Builds the screen once, when the shell starts. The context is how the screen reads the selected game and asks the shell to do things.</summary>
    IScreen Create(IShellContext shell);
}
