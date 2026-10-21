namespace Savepoint.Core.Catalog;

/// <summary>A ready-made game definition for a well-known game, offered by the Add game flow.</summary>
/// <param name="Id">The template's own id, taken from the id inside <paramref name="Definition"/>.</param>
/// <param name="DisplayName">The game's own name; Core carries no UI copy.</param>
/// <param name="Definition">The definition as shipped. Its id is fixed, so use <see cref="ToDraft"/> to get one that can be added.</param>
public sealed record GameTemplate(string Id, string DisplayName, GameDefinition Definition)
{
    /// <summary>
    /// Returns a copy of the definition with a fresh game id. A template's fixed id would collide on a second use, so a draft
    /// always gets a new one. Nothing is saved.
    /// </summary>
    /// <param name="random">The source of the id's random suffix; defaults to a shared instance, and tests pass a seeded one.</param>
    public GameDefinition ToDraft(Random? random = null) =>
        Definition with { Id = CatalogIds.NewGameId(Definition.Name, random) };
}
