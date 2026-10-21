using System.Text.Json.Serialization;

namespace Savepoint.Core.Catalog;

[JsonConverter(typeof(JsonStringEnumConverter<ExternalRole>))]
public enum ExternalRole
{
    [JsonStringEnumMemberName("saves")]
    Saves,
}
