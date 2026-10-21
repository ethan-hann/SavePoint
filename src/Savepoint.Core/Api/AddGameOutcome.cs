using Savepoint.Core.Catalog;

namespace Savepoint.Core.Api;

// Finding old history is a question for the user, not an error, so it is an outcome of its own.
public abstract record AddGameOutcome
{
    private AddGameOutcome()
    {
    }

    public sealed record Added(GameDefinition Game) : AddGameOutcome;

    // Nothing was saved; answer with IGamesApi.ResolveOrphanAsync.
    public sealed record HistoryFound(OrphanedHistory History) : AddGameOutcome;
}
