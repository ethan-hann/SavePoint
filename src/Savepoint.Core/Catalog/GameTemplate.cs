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
    /// always gets a new one. The draft also leaves out logs and crash dumps (<see cref="RunTimeOutput.DefaultRules"/>) from the install folder
    /// only, beside the template's own rules, so a new game does not mistake a log the game rewrites for an update and a saves folder
    /// is never touched by them. On Windows the saves folder is the template's own default unless it names one for Windows. On Linux
    /// and macOS it is only the folder the template names for that system, and null (left for the player to pick) otherwise,
    /// because the Windows default is not where the game saves there. The mods folder follows the same rule through <c>modsByOs</c>.
    /// Nothing is saved.
    /// </summary>
    /// <param name="os">The operating system the game will be added on.</param>
    /// <param name="random">The source of the id's random suffix; defaults to a shared instance, and tests pass a seeded one.</param>
    public GameDefinition ToDraft(OsKind os, Random? random = null)
    {
        var draft = Definition with
        {
            Id = CatalogIds.NewGameId(Definition.Name, random),
            InstallIgnorePaths = [.. RunTimeOutput.Merge(Definition.InstallIgnorePaths, RunTimeOutput.DefaultRules.Where(rule => !Definition.IgnorePaths.Contains(rule)))],
        };
        var key = os switch { OsKind.Windows => "windows", OsKind.MacOS => "macos", _ => "linux" };
        return draft with
        {
            External = Hints.SavesByOs.TryGetValue(key, out var saves) ? saves : DefaultOnly(os, draft.External),
            Mods = Hints.ModsByOs.TryGetValue(key, out var mods) ? mods : DefaultOnly(os, draft.Mods),
        };
    }

    // The default is the Windows folder. A token that exists elsewhere says nothing about where the game keeps its files there (a
    // Windows game under Proton keeps them inside its prefix), so the folder stays blank for the player to pick.
    private static T? DefaultOnly<T>(OsKind os, T? windowsDefault)
        where T : class =>
        os == OsKind.Windows ? windowsDefault : null;
}
