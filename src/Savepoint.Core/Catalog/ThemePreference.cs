using System.Text.Json.Serialization;

namespace Savepoint.Core.Catalog;

/// <summary>Whether the app is drawn light or dark.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ThemePreference>))]
public enum ThemePreference
{
    /// <summary>Follows the OS setting and changes with it; stored as <c>"system"</c>. The default.</summary>
    [JsonStringEnumMemberName("system")]
    System,

    /// <summary>Always light; stored as <c>"light"</c>.</summary>
    [JsonStringEnumMemberName("light")]
    Light,

    /// <summary>Always dark; stored as <c>"dark"</c>.</summary>
    [JsonStringEnumMemberName("dark")]
    Dark,
}
