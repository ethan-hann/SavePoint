using System.Text.Json.Serialization;

namespace Savepoint.Core.Catalog;

/// <summary>Which of a game's tracked folders something refers to. Each folder has its own repository.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RepoRole>))]
public enum RepoRole
{
    /// <summary>The game's install folder: its program files and mods. Stored as <c>"install"</c>.</summary>
    [JsonStringEnumMemberName("install")]
    Install,

    /// <summary>The optional folder outside the install that holds the game's saves. Stored as <c>"external"</c>.</summary>
    [JsonStringEnumMemberName("external")]
    External,

    /// <summary>The optional folder outside the install that holds the game's mods. Stored as <c>"mods"</c>.</summary>
    [JsonStringEnumMemberName("mods")]
    Mods,
}
