namespace Savepoint.Core.Errors;

/// <summary>
/// The player asked SavePoint to start the Steam client, and it did not come up signed in within the time allowed
/// (<c>error.launch.steamDidNotStart</c>). Nothing was read or switched. Args: <c>gameId</c>.
/// </summary>
public sealed class SteamDidNotStartException : SavepointException
{
    /// <summary>Creates the error.</summary>
    /// <param name="gameId">The game that was waiting for Steam.</param>
    public SteamDidNotStartException(string gameId)
        : base(
            "error.launch.steamDidNotStart",
            $"The Steam client did not finish starting for game '{gameId}'.",
            new Dictionary<string, object?> { ["gameId"] = gameId })
    {
    }
}
