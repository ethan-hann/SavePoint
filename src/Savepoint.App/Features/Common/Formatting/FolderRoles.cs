using Savepoint.Core.Catalog;

namespace Savepoint.App.Features.Common.Formatting;

/// <summary>How a game's three tracked folders are named in catalog messages: the value a message's <c>select</c> on <c>role</c>, <c>folder</c> or <c>part</c> branches on.</summary>
public static class FolderRoles
{
    /// <summary>The text a message selects on for a folder: <c>install</c>, <c>mods</c> or <c>external</c> (the saves folder).</summary>
    /// <param name="role">The folder.</param>
    public static string Name(RepoRole role) => role switch
    {
        RepoRole.Install => "install",
        RepoRole.Mods => "mods",
        _ => "external",
    };

    /// <summary>The text a message's <c>hasMods</c> select branches on: <c>yes</c> when the game has a mods folder of its own, so the game folder's wording must not claim the mods.</summary>
    /// <param name="hasMods">Whether the game has a mods folder.</param>
    public static string HasMods(bool hasMods) => hasMods ? "yes" : "no";

    /// <summary>Where a folder sits in lists: the game folder, the mods folder, then the saves folder, the order a switch writes them and the editor shows them.</summary>
    /// <param name="role">The folder.</param>
    public static int Order(RepoRole role) => role switch
    {
        RepoRole.Install => 0,
        RepoRole.Mods => 1,
        _ => 2,
    };

    /// <summary>The folder a name stands for, as the Api writes it in error arguments; null for any other text.</summary>
    /// <param name="name">The text, or a <see cref="RepoRole"/> already.</param>
    public static RepoRole? Parse(object? name) => name switch
    {
        RepoRole role => role,
        "install" => RepoRole.Install,
        "mods" => RepoRole.Mods,
        "external" => RepoRole.External,
        _ => null,
    };
}
