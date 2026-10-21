using System.Text.Json;

namespace Savepoint.Core.Catalog;

public static class GameTemplates
{
    private const string _resourcePrefix = "Savepoint.Core.Catalog.Templates.";

    private static readonly string[] _files = ["skyrim-se.json", "simple-indie-example.json"];

    // The display name is the game's own name, so Core carries no UI copy.
    public static IReadOnlyList<GameTemplate> All { get; } = Load();

    private static List<GameTemplate> Load()
    {
        var assembly = typeof(GameTemplates).Assembly;
        var templates = new List<GameTemplate>();
        foreach (var file in _files)
        {
            using var stream = assembly.GetManifestResourceStream(_resourcePrefix + file)
                ?? throw new InvalidOperationException($"Embedded template '{file}' is missing from the assembly.");
            var definition = JsonSerializer.Deserialize(stream, CatalogJsonContext.Default.GameDefinition)
                ?? throw new InvalidOperationException($"Embedded template '{file}' is empty.");
            templates.Add(new GameTemplate(definition.Id, definition.Name, definition));
        }

        return templates;
    }
}
