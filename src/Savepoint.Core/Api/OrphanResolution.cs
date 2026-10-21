namespace Savepoint.Core.Api;

/// <summary>The user's answer when adding a game finds old history. The cases are closed: reconnect or start fresh, nothing else.</summary>
public abstract record OrphanResolution
{
    private OrphanResolution()
    {
    }

    /// <summary>Leaves the old history on disk but unreachable, and adds the game under its new id.</summary>
    public sealed record StartFresh() : OrphanResolution;

    /// <summary>Adds the game under the old id and restores the listed profiles from the old history.</summary>
    /// <param name="ExistingGameId">The id of the removed game, from <see cref="OrphanedHistory.ExistingGameId"/>.</param>
    /// <param name="Profiles">The profiles to restore, each with the name the user will see.</param>
    public sealed record Reconnect(string ExistingGameId, IReadOnlyList<RecoveredProfile> Profiles) : OrphanResolution;
}
