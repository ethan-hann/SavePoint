using System.Text.Json.Serialization;

namespace Savepoint.Core.Catalog;

/// <summary>How the main window arranges navigation and the game list.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ShellLayout>))]
public enum ShellLayout
{
    /// <summary>A navigation rail down the left side with the game list in it; stored as <c>"sidebar"</c>. The default.</summary>
    [JsonStringEnumMemberName("sidebar")]
    Sidebar,

    /// <summary>A top bar with the games as a shelf above the content; stored as <c>"library"</c>.</summary>
    [JsonStringEnumMemberName("library")]
    Library,
}
