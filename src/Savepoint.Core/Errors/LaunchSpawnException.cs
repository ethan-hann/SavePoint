using Savepoint.Core.Catalog;

namespace Savepoint.Core.Errors;

/// <summary>The OS refused to start the game or hand its link to the opener; the game is not running.</summary>
public sealed class LaunchSpawnException : SavepointException
{
    /// <summary>Creates the error for one launch method.</summary>
    /// <param name="method">Whether a program or a link was being started; the copy can branch on it.</param>
    /// <param name="devMessage">English text for the log: what was started and why it failed.</param>
    /// <param name="inner">The exception the OS call threw, when there was one.</param>
    public LaunchSpawnException(LaunchMethod method, string devMessage, Exception? inner)
        : base(
            "error.launch.spawnFailed",
            devMessage,
            new Dictionary<string, object?> { ["method"] = method },
            inner)
    {
    }
}
