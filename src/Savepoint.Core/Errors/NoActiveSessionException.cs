namespace Savepoint.Core.Errors;

public sealed class NoActiveSessionException : SavepointException
{
    public NoActiveSessionException(string gameId)
        : base(
            "error.launch.noActiveSession",
            $"Game '{gameId}' has no active play session.",
            new Dictionary<string, object?> { ["gameId"] = gameId })
    {
    }
}
