namespace Savepoint.Core.Errors;

public sealed class LaunchAlreadyRunningException : SavepointException
{
    public LaunchAlreadyRunningException(string gameId)
        : base(
            "error.launch.alreadyRunning",
            $"A launch is already running for game '{gameId}'.",
            new Dictionary<string, object?> { ["gameId"] = gameId })
    {
    }
}
