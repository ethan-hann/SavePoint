using Savepoint.Core.Backend;

namespace Savepoint.Core.Errors;

/// <summary>
/// SavePoint is switching the game's files without playing, which blocks starting the game, another switch, deleting the game or a
/// profile, discarding changes, moving storage, and clearing all data until the switch ends (<c>error.gameService.switching</c>).
/// The game itself is not running. Args: <c>gameId</c>, <c>gameName</c>.
/// </summary>
public sealed class SwitchInProgressException : SavepointException
{
    /// <summary>Creates the error.</summary>
    /// <param name="gameId">The id of the game being switched.</param>
    /// <param name="gameName">Its name, for the message the user reads.</param>
    public SwitchInProgressException(string gameId, string gameName)
        : base(
            "error.gameService.switching",
            $"Game '{gameName}' ({gameId}) is being switched.",
            new Dictionary<string, object?> { ["gameId"] = gameId, ["gameName"] = gameName })
    {
    }

    /// <summary>
    /// The refusal for a game that is taken: <see cref="SwitchInProgressException"/> while a switch holds it, otherwise
    /// <see cref="GameRunningException"/>.
    /// </summary>
    /// <param name="sessions">What the services know about the game's sessions.</param>
    /// <param name="gameId">The game that is taken.</param>
    /// <param name="gameName">Its name.</param>
    public static SavepointException Refusal(IGameSessions sessions, string gameId, string gameName)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        return sessions.IsSwitching(gameId) ? new SwitchInProgressException(gameId, gameName) : new GameRunningException(gameId, gameName);
    }
}
