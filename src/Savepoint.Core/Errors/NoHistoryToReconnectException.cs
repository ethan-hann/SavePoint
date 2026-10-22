namespace Savepoint.Core.Errors;

/// <summary>
/// Reconnecting a game's old history was asked for, but no saved history is there under that id at the game's folders
/// (<c>error.gameService.noHistoryToReconnect</c>). Args: <c>gameId</c>.
/// </summary>
public sealed class NoHistoryToReconnectException : SavepointException
{
    /// <summary>Creates the error.</summary>
    /// <param name="gameId">The id whose history was to be reconnected.</param>
    public NoHistoryToReconnectException(string gameId)
        : base(
            "error.gameService.noHistoryToReconnect",
            $"There is no saved history for '{gameId}' at these folders to reconnect.",
            new Dictionary<string, object?> { ["gameId"] = gameId })
    {
    }
}
