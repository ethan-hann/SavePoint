namespace Savepoint.Core.Errors;

/// <summary>
/// Steam was already updating the game, or had just changed its build, when the launch began (<c>error.launch.steamUpdatingBeforeSwitch</c>),
/// so nothing was switched and the program was not started. The folders are as the player left them. Args: <c>gameId</c>.
/// </summary>
public sealed class SteamUpdatingBeforeSwitchException : SavepointException
{
    /// <summary>Creates the error.</summary>
    /// <param name="gameId">The game Steam is updating.</param>
    public SteamUpdatingBeforeSwitchException(string gameId)
        : base(
            "error.launch.steamUpdatingBeforeSwitch",
            $"Steam is updating game '{gameId}', so the launch stopped before any folder was switched.",
            new Dictionary<string, object?> { ["gameId"] = gameId })
    {
    }
}
