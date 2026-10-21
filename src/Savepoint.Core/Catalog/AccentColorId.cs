using System.Text.Json.Serialization;

namespace Savepoint.Core.Catalog;

[JsonConverter(typeof(JsonStringEnumConverter<AccentColorId>))]
public enum AccentColorId
{
    [JsonStringEnumMemberName("blue")]
    Blue,

    [JsonStringEnumMemberName("violet")]
    Violet,

    [JsonStringEnumMemberName("green")]
    Green,

    [JsonStringEnumMemberName("amber")]
    Amber,

    [JsonStringEnumMemberName("crimson")]
    Crimson,
}
