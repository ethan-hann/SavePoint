using System.Text.Json.Serialization;

namespace Savepoint.Core.Catalog;

/// <summary>
/// Source-generated JSON serialization for the catalog models: camelCase names, indented output, and null fields left out, which
/// keeps optional fields absent as in the v1 files. Used by <see cref="CatalogStore"/> and the embedded templates.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(GameDefinition))]
[JsonSerializable(typeof(Profile))]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(List<GameDefinition>))]
[JsonSerializable(typeof(List<Profile>))]
public sealed partial class CatalogJsonContext : JsonSerializerContext
{
}
