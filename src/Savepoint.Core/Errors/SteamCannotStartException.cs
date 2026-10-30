namespace Savepoint.Core.Errors;

/// <summary>
/// The player asked SavePoint to start the Steam client, and the client could not be started at all, for example because the
/// <c>steam</c> command is not on the path (<c>error.launch.steamCannotStart</c>). Nothing was read or switched. Args: <c>gameId</c>.
/// </summary>
public sealed class SteamCannotStartException : SavepointException
{
    /// <summary>Creates the error.</summary>
    /// <param name="gameId">The game that was waiting for Steam.</param>
    public SteamCannotStartException(string gameId)
        : base(
            "error.launch.steamCannotStart",
            $"The Steam client could not be started for game '{gameId}'.",
            new Dictionary<string, object?> { ["gameId"] = gameId })
    {
    }
}
