namespace Savepoint.Core.Errors;

public sealed class LaunchSpawnException : SavepointException
{
    public LaunchSpawnException(string method, string devMessage, Exception? inner)
        : base(
            "error.launch.spawnFailed",
            devMessage,
            new Dictionary<string, object?> { ["method"] = method },
            inner)
    {
    }
}
