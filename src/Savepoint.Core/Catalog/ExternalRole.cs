using System.Text.Json.Serialization;

namespace Savepoint.Core.Catalog;

/// <summary>What a game's external folder holds. Only saves exist today; the field is kept so other kinds can be added.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ExternalRole>))]
public enum ExternalRole
{
    /// <summary>The game's save files, stored as <c>"saves"</c>.</summary>
    [JsonStringEnumMemberName("saves")]
    Saves,
}
