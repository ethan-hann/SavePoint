using Savepoint.Core.Platform;

namespace Savepoint.Core.Catalog;

/// <summary>A ready-made game definition for a well-known game, offered by the Add game flow and used to recognize games in a store library.</summary>
/// <param name="Id">The template's own id, taken from the id inside <paramref name="Definition"/>.</param>
/// <param name="DisplayName">The game's own name; Core carries no UI copy.</param>
/// <param name="Definition">The definition as shipped. Its id is fixed, so use <see cref="ToDraft(Random?)"/> to get one that can be added.</param>
/// <param name="Hints">How the game looks in a store library; empty when the template says nothing.</param>
public sealed record GameTemplate(string Id, string DisplayName, GameDefinition Definition, TemplateLibraryHints? Hints = null)
{
    /// <summary>How the game looks in a store library; never null.</summary>
    public TemplateLibraryHints Hints { get; init; } = Hints ?? new();

    /// <summary>
    /// Returns a copy of the definition with a fresh game id for the operating system this process runs on. Nothing is saved.
    /// </summary>
    /// <param name="random">The source of the id's random suffix; defaults to a shared instance, and tests pass a seeded one.</param>
    public GameDefinition ToDraft(Random? random = null) => ToDraft(PlatformFactory.CurrentOs, random);

    /// <summary>
    /// Returns a copy of the definition with a fresh game id. A template's fixed id would collide on a second use, so a draft
    /// always gets a new one. The saves folder is the one the template names for <paramref name="os"/> when it names one, and the
    /// definition's own otherwise. Nothing is saved.
    /// </summary>
    /// <param name="os">The operating system the game will be added on.</param>
    /// <param name="random">The source of the id's random suffix; defaults to a shared instance, and tests pass a seeded one.</param>
    public GameDefinition ToDraft(OsKind os, Random? random = null)
    {
        var draft = Definition with { Id = CatalogIds.NewGameId(Definition.Name, random) };
        var key = os switch { OsKind.Windows => "windows", OsKind.MacOS => "macos", _ => "linux" };
        return Hints.SavesByOs.TryGetValue(key, out var saves) ? draft with { External = saves } : draft;
    }
}
