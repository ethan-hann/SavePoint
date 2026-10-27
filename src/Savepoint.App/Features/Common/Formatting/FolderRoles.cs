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
