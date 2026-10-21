using System.Text.Json.Serialization;

namespace Savepoint.Core.Catalog;

[JsonConverter(typeof(JsonStringEnumConverter<ShellLayout>))]
public enum ShellLayout
{
    [JsonStringEnumMemberName("sidebar")]
    Sidebar,

    [JsonStringEnumMemberName("library")]
    Library,
}
