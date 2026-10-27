namespace Savepoint.Core.Errors;

/// <summary>
/// A game that quits when the Steam client is not running was asked to start while Steam is closed or signed out
/// (<c>error.launch.steamNotRunning</c>). Nothing was read or switched. Args: <c>gameId</c>.
/// </summary>
public sealed class SteamNotRunningException : SavepointException
{
    /// <summary>Creates the error.</summary>
    /// <param name="gameId">The game that needs Steam.</param>
    public SteamNotRunningException(string gameId)
        : base(
            "error.launch.steamNotRunning",
            $"Game '{gameId}' needs the Steam client to be running and signed in, and it is not.",
            new Dictionary<string, object?> { ["gameId"] = gameId })
    {
    }
}
