namespace Savepoint.Core.Errors;

/// <summary>
/// A call named a game the catalog does not hold, such as one removed in the meantime (<c>error.game.notFound</c>). Args:
/// <c>gameId</c>.
/// </summary>
public sealed class GameNotFoundException : SavepointException
{
    /// <summary>Creates the error.</summary>
    /// <param name="gameId">The id that matched no game.</param>
    public GameNotFoundException(string gameId)
        : base(
            "error.game.notFound",
            $"There is no game '{gameId}'.",
            new Dictionary<string, object?> { ["gameId"] = gameId })
    {
    }
}
