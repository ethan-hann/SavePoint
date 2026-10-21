namespace Savepoint.Core.Errors;

/// <summary>
/// A game's folders are the same as, or inside, another game's folders, so two games would track the same files
/// (<c>error.gameService.pathOverlap</c>). Adding or updating a game refuses it. Args: <c>conflictingGameId</c>,
/// <c>conflictingGameName</c>.
/// </summary>
public sealed class GameOverlapException : SavepointException
{
    /// <summary>Creates the error.</summary>
    /// <param name="conflictingGameId">The id of the game that already holds the folder.</param>
    /// <param name="conflictingGameName">That game's name, for the message the user reads.</param>
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
