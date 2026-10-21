using System.Text.Json.Serialization;

namespace Savepoint.Core.Catalog;

/// <summary>How SavePoint starts a game.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LaunchMethod>))]
public enum LaunchMethod
{
    /// <summary>Starts a program (or, on macOS, an app bundle) from the install folder; stored as <c>"exe"</c>.</summary>
    [JsonStringEnumMemberName("exe")]
    Exe,

    /// <summary>Hands a link such as a storefront URI to the OS opener; stored as <c>"uri"</c>.</summary>
    [JsonStringEnumMemberName("uri")]
    Uri,
}
