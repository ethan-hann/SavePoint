namespace Savepoint.Core.Errors;

/// <summary>
/// Steam is updating the game, or changed its install while the launch ran (<c>error.launch.steamUpdatingGame</c>), so the program
/// was not started: the update may have written over the version the launch had just loaded. The folders were switched; the game was
/// not started. Args: <c>gameId</c>.
/// </summary>
public sealed class SteamUpdatingGameException : SavepointException
{
    /// <summary>Creates the error.</summary>
    /// <param name="gameId">The game Steam is updating.</param>
    public SteamUpdatingGameException(string gameId)
        : base(
            "error.launch.steamUpdatingGame",
            $"Steam is updating game '{gameId}', or changed it while the launch ran, so the program was not started.",
            new Dictionary<string, object?> { ["gameId"] = gameId })
    {
    }
}
