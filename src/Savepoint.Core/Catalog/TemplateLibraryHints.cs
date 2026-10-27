namespace Savepoint.Core.Catalog;

/// <summary>
/// What a template says about how its game looks in a store library, so a scan of installed games can recognize it. Every member is
/// optional; a template with none still matches by its Steam app id, when it has one. Read from the template file's
/// <c>libraryHints</c> block and never stored with a game.
/// </summary>
/// <param name="SteamAppIds">Extra Steam app ids that are this game, such as another edition, besides the definition's own <see cref="GameDefinition.SteamAppId"/>.</param>
/// <param name="SteamInstallDirs">Folder names under <c>steamapps/common</c> the game installs into, compared without regard to case.</param>
/// <param name="GogIds">GOG product ids, as text, for the game and its editions.</param>
/// <param name="GogFolders">Folder names the GOG installer uses for the game, compared without regard to case.</param>
/// <param name="SavesByOs">The saves folder to use on one operating system instead of the definition's own, keyed by <c>windows</c>, <c>linux</c>, or <c>macos</c>.</param>
/// <param name="ModsByOs">The mods folder to use on one operating system instead of the definition's own, keyed like <paramref name="SavesByOs"/>.</param>
public sealed record TemplateLibraryHints(
    IReadOnlyList<int>? SteamAppIds = null,
    IReadOnlyList<string>? SteamInstallDirs = null,
    IReadOnlyList<string>? GogIds = null,
    IReadOnlyList<string>? GogFolders = null,
    IReadOnlyDictionary<string, ExternalLocation>? SavesByOs = null,
    IReadOnlyDictionary<string, ModsLocation>? ModsByOs = null)
{
    /// <summary>The extra Steam app ids; empty, never null, when the file had none.</summary>
    // Redeclared so an absent JSON value reads as empty instead of null.
    public IReadOnlyList<int> SteamAppIds { get; init; } = SteamAppIds ?? [];

    /// <summary>The Steam folder names; empty, never null, when the file had none.</summary>
    public IReadOnlyList<string> SteamInstallDirs { get; init; } = SteamInstallDirs ?? [];

    /// <summary>The GOG product ids; empty, never null, when the file had none.</summary>
    public IReadOnlyList<string> GogIds { get; init; } = GogIds ?? [];

    /// <summary>The GOG folder names; empty, never null, when the file had none.</summary>
    public IReadOnlyList<string> GogFolders { get; init; } = GogFolders ?? [];

    /// <summary>The per-system saves folders; empty, never null, when the file had none.</summary>
    public IReadOnlyDictionary<string, ExternalLocation> SavesByOs { get; init; } = SavesByOs ?? new Dictionary<string, ExternalLocation>();

    /// <summary>The per-system mods folders; empty, never null, when the file had none.</summary>
    public IReadOnlyDictionary<string, ModsLocation> ModsByOs { get; init; } = ModsByOs ?? new Dictionary<string, ModsLocation>();
}
