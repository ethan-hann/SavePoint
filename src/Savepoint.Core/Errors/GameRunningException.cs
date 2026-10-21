namespace Savepoint.Core.Errors;

/// <summary>
/// The game is running, which blocks deleting the game or a profile, moving storage, and clearing all data until the session ends
/// (<c>error.gameService.gameRunning</c>, D13.5). Args: <c>gameId</c>, <c>gameName</c>.
/// </summary>
public sealed class GameRunningException : SavepointException
{
    /// <summary>Creates the error.</summary>
    /// <param name="gameId">The id of the running game.</param>
    /// <param name="gameName">Its name, for the message the user reads.</param>
    public GameRunningException(string gameId, string gameName)
        : base(
            "error.gameService.gameRunning",
            $"Game '{gameName}' ({gameId}) is running.",
            new Dictionary<string, object?> { ["gameId"] = gameId, ["gameName"] = gameName })
    {
    }
}
