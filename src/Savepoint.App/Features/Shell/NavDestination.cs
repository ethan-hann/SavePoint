namespace Savepoint.App.Features.Shell;

/// <summary>The top-level places the shell can show.</summary>
public enum NavDestination
{
    /// <summary>A game's profiles and everything done from them.</summary>
    Games,

    /// <summary>The points of one game that can be gone back to.</summary>
    History,

    /// <summary>The running and recent jobs of one game or of every game, their details, and a way to stop them.</summary>
    Activity,

    /// <summary>Storage, appearance and the end-of-session choices.</summary>
    Settings,
}
