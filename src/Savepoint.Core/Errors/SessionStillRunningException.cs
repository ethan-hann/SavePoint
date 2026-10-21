namespace Savepoint.Core.Errors;

/// <summary>
/// The session cannot be completed yet because the game has not exited (<c>error.launch.sessionStillRunning</c>); "I'm done playing"
/// ends the wait first. Args: <c>gameId</c>.
/// </summary>
public sealed class SessionStillRunningException : SavepointException
{
    /// <summary>Creates the error.</summary>
    /// <param name="gameId">The game whose session is still running.</param>
    public SessionStillRunningException(string gameId)
        : base(
            "error.launch.sessionStillRunning",
            $"The play session for game '{gameId}' is still running.",
            new Dictionary<string, object?> { ["gameId"] = gameId })
    {
    }
}
