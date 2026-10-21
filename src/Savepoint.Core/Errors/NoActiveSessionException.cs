namespace Savepoint.Core.Errors;

/// <summary>
/// A session call named a game with no session to act on: none was started, or it has already completed or failed
/// (<c>error.launch.noActiveSession</c>). Args: <c>gameId</c>.
/// </summary>
public sealed class NoActiveSessionException : SavepointException
{
    /// <summary>Creates the error.</summary>
    /// <param name="gameId">The game that has no open session.</param>
    public NoActiveSessionException(string gameId)
        : base(
            "error.launch.noActiveSession",
            $"Game '{gameId}' has no active play session.",
            new Dictionary<string, object?> { ["gameId"] = gameId })
    {
    }
}
