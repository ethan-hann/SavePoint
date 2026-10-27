using System.Collections.Frozen;
using System.Text.Json;

namespace Savepoint.Core.Catalog;

/// <summary>The game templates embedded in the assembly, read once on first use.</summary>
public static class GameTemplates
{
    private const string _resourcePrefix = "Savepoint.Core.Catalog.Templates.";

    /// <summary>
    /// Every embedded template, sorted by id so the order never depends on the build. The display name is the game's own name, so Core carries no UI copy. A template leaves
    /// the install folder empty for the player to pick, because no path works on every operating system. The lists inside each
    /// definition are read-only copies.
    /// </summary>
    public static IReadOnlyList<GameTemplate> All { get; } = Load();

    private static List<GameTemplate> Load()
    {
        var assembly = typeof(GameTemplates).Assembly;
        var templates = new List<GameTemplate>();
        var files = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(_resourcePrefix, StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);
        foreach (var file in files)
        {
            using var stream = assembly.GetManifestResourceStream(file)
                ?? throw new InvalidOperationException($"Embedded template '{file}' is missing from the assembly.");
            using var reader = new StreamReader(stream);
            templates.Add(Read(reader.ReadToEnd()));
        }

        // Ids, not file names, set the order: "-" sorts before "." so the two disagree.
        return [.. templates.OrderBy(template => template.Id, StringComparer.Ordinal)];
    }

    // Reads one template file: the definition, and the optional library hints that sit beside it.
    internal static GameTemplate Read(string json)
    {
        var definition = JsonSerializer.Deserialize(json, CatalogJsonContext.Default.GameDefinition)
            ?? throw new InvalidOperationException("A template is empty.");
        var shared = ReadOnlyLists(definition);
        var hints = JsonSerializer.Deserialize(json, CatalogJsonContext.Default.TemplateFileHints)?.LibraryHints ?? new TemplateLibraryHints();
        return new GameTemplate(shared.Id, shared.Name, shared, ReadOnlyLists(hints));
    }

    private static TemplateLibraryHints ReadOnlyLists(TemplateLibraryHints hints) => hints with
    {
        SteamAppIds = [.. hints.SteamAppIds],
        SteamInstallDirs = [.. hints.SteamInstallDirs],
        GogIds = [.. hints.GogIds],
        GogFolders = [.. hints.GogFolders],
        SavesByOs = hints.SavesByOs.ToFrozenDictionary(pair => pair.Key.ToLowerInvariant(), pair => pair.Value),
        ModsByOs = hints.ModsByOs.ToFrozenDictionary(pair => pair.Key.ToLowerInvariant(), pair => pair.Value),
    };

    // Drafts copy the definition but share its lists, so the lists must not be changeable through a draft.
    private static GameDefinition ReadOnlyLists(GameDefinition definition) => definition with
    {
        IgnorePaths = [.. definition.IgnorePaths],
        InstallIgnorePaths = [.. definition.InstallIgnorePaths],
        ModPathHints = [.. definition.ModPathHints],
        Launch = definition.Launch with { Args = [.. definition.Launch.Args] },
    };
}
