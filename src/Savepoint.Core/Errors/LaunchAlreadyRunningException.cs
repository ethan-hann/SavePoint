namespace Savepoint.Core.Errors;

/// <summary>
/// A launch was asked for a game whose launch or play session has not ended (<c>error.launch.alreadyRunning</c>). Args: <c>gameId</c>.
/// </summary>
public sealed class LaunchAlreadyRunningException : SavepointException
{
    /// <summary>Creates the error.</summary>
    /// <param name="gameId">The game that already has an open session.</param>
    public LaunchAlreadyRunningException(string gameId)
        : base(
            "error.launch.alreadyRunning",
            $"A launch is already running for game '{gameId}'.",
            new Dictionary<string, object?> { ["gameId"] = gameId })
    {
    }
}
