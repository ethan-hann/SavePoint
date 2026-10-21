namespace Savepoint.Core.Errors;

public sealed class GameRunningException : SavepointException
{
    public GameRunningException(string gameId, string gameName)
        : base(
            "error.gameService.gameRunning",
            $"Game '{gameName}' ({gameId}) is running.",
            new Dictionary<string, object?> { ["gameId"] = gameId, ["gameName"] = gameName })
    {
    }
}
