using Savepoint.Core.Catalog;

namespace Savepoint.Core.Api;

/// <summary>What adding a game came to. Finding old history is a question for the user, not an error, so it is an outcome of its own.</summary>
public abstract record AddGameOutcome
{
    private AddGameOutcome()
    {
    }

    /// <summary>The game was added.</summary>
    /// <param name="Game">The game as stored, after normalization.</param>
    public sealed record Added(GameDefinition Game) : AddGameOutcome;

    /// <summary>The folders hold history from a removed game. Nothing was saved; answer with <see cref="IGamesApi.ResolveOrphanAsync"/>.</summary>
    /// <param name="History">What was found.</param>
    public sealed record HistoryFound(OrphanedHistory History) : AddGameOutcome;
}
