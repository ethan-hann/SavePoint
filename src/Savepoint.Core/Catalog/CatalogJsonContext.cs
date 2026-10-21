using System.Text.Json.Serialization;

namespace Savepoint.Core.Catalog;

// Omitting nulls keeps optional fields absent, the same as the v1 files.
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
