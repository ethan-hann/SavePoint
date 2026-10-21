using System.Text.Json.Serialization;

namespace Savepoint.Core.Catalog;

/// <summary>The accent presets the user can pick. Each is the seed color of the generated Material palette (D22).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AccentColorId>))]
public enum AccentColorId
{
    /// <summary>Blue, stored as <c>"blue"</c>. The default accent.</summary>
    [JsonStringEnumMemberName("blue")]
    Blue,

    /// <summary>Violet, stored as <c>"violet"</c>.</summary>
    [JsonStringEnumMemberName("violet")]
    Violet,

    /// <summary>Green, stored as <c>"green"</c>.</summary>
    [JsonStringEnumMemberName("green")]
    Green,

    /// <summary>Amber, stored as <c>"amber"</c>.</summary>
    [JsonStringEnumMemberName("amber")]
    Amber,

    /// <summary>Crimson, stored as <c>"crimson"</c>.</summary>
    [JsonStringEnumMemberName("crimson")]
    Crimson,
}
