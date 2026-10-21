namespace Savepoint.Core.Errors;

public sealed class GameOverlapException : SavepointException
{
    public GameOverlapException(string conflictingGameId, string conflictingGameName)
        : base(
            "error.gameService.pathOverlap",
            $"The paths overlap game '{conflictingGameName}' ({conflictingGameId}).",
            new Dictionary<string, object?>
            {
                ["conflictingGameId"] = conflictingGameId,
                ["conflictingGameName"] = conflictingGameName,
            })
    {
    }
}
