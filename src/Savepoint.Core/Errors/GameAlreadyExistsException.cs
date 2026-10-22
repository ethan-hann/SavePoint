namespace Savepoint.Core.Errors;

/// <summary>
/// A game with that id is already in the library, so adding it again is refused (<c>error.gameService.alreadyExists</c>).
/// Args: <c>gameId</c>.
/// </summary>
public sealed class GameAlreadyExistsException : SavepointException
{
    /// <summary>Creates the error.</summary>
    /// <param name="gameId">The id that is already taken.</param>
    public GameAlreadyExistsException(string gameId)
        : base(
            "error.gameService.alreadyExists",
            $"There is already a game '{gameId}'.",
            new Dictionary<string, object?> { ["gameId"] = gameId })
    {
    }
}
