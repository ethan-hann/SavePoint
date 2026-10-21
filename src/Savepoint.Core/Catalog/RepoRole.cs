using System.Text.Json.Serialization;

namespace Savepoint.Core.Catalog;

[JsonConverter(typeof(JsonStringEnumConverter<RepoRole>))]
public enum RepoRole
{
    [JsonStringEnumMemberName("install")]
    Install,

    [JsonStringEnumMemberName("external")]
    External,
}
