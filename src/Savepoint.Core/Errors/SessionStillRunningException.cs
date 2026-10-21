namespace Savepoint.Core.Errors;

public sealed class SessionStillRunningException : SavepointException
{
    public SessionStillRunningException(string gameId)
        : base(
            "error.launch.sessionStillRunning",
            $"The play session for game '{gameId}' is still running.",
            new Dictionary<string, object?> { ["gameId"] = gameId })
    {
    }
}
