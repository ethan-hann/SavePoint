namespace Savepoint.Core.Catalog;

public sealed record GameTemplate(string Id, string DisplayName, GameDefinition Definition)
{
    // A template's fixed id would collide on a second import, so a draft always gets a fresh one. Nothing is saved here.
    public GameDefinition ToDraft(Random? random = null) =>
        Definition with { Id = CatalogIds.NewGameId(Definition.Name, random) };
}
