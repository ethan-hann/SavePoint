using Savepoint.Core.Api;

namespace Savepoint.Core.Errors;

/// <summary>
/// The session was completed with a choice it did not offer, for example moving a profile forward from a session that did not begin at
/// an older point (<c>error.launch.completionNotOffered</c>). Nothing was saved and the session still waits for a choice. Args:
/// <c>gameId</c>, <c>choice</c>.
/// </summary>
public sealed class CompletionNotOfferedException : SavepointException
{
    /// <summary>Creates the error.</summary>
    /// <param name="gameId">The game.</param>
    /// <param name="choice">The choice that was not among the session's options.</param>
    public CompletionNotOfferedException(string gameId, CompletionChoice choice)
        : base(
            "error.launch.completionNotOffered",
            $"The session of game '{gameId}' did not offer the choice '{choice}'.",
            new Dictionary<string, object?> { ["gameId"] = gameId, ["choice"] = choice })
    {
    }
}
