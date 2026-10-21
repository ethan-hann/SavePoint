namespace Savepoint.Core.Api;

public abstract record OrphanResolution
{
    // Closed: reconnect or start fresh, nothing else.
    private OrphanResolution()
    {
    }

    // Leaves the old history on disk but unreachable, and adds the game under its new id.
    public sealed record StartFresh() : OrphanResolution;

    // Adds the game under the old id and restores the listed profiles.
    public sealed record Reconnect(string ExistingGameId, IReadOnlyList<RecoveredProfile> Profiles) : OrphanResolution;
}
