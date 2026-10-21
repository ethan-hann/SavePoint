using Savepoint.Core.Catalog;

namespace Savepoint.Core.Errors;

/// <summary>
/// The folder is not where the play session left it: the files were switched to something else while the session ran, so
/// new progress cannot be kept on top of the point the session started from. Nothing was recorded or moved.
/// </summary>
public sealed class SessionMovedException : SavepointException
{
    /// <summary>Builds the error for a folder that is no longer on the session's point.</summary>
    /// <param name="role">Which of the game's folders it is.</param>
    /// <param name="devMessage">What differs, in English, for logs.</param>
    public SessionMovedException(RepoRole role, string devMessage)
        : base(
            "error.launch.sessionMoved",
            devMessage,
            new Dictionary<string, object?> { ["role"] = role })
    {
    }
}
