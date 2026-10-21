using System.Text.Json.Serialization;

namespace Savepoint.Core.Catalog;

[JsonConverter(typeof(JsonStringEnumConverter<LaunchMethod>))]
public enum LaunchMethod
{
    [JsonStringEnumMemberName("exe")]
    Exe,

    [JsonStringEnumMemberName("uri")]
    Uri,
}
