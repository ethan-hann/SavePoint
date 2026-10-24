using Savepoint.Core.Catalog;

namespace Savepoint.Core.Api;

/// <summary>The store a library game was found in.</summary>
public enum LibrarySource
{
    /// <summary>The Steam client's libraries.</summary>
    Steam,

    /// <summary>GOG Galaxy's installed games.</summary>
    Gog,
}

/// <summary>One installed game found in a store library, with what SavePoint would add for it.</summary>
/// <param name="Key">A stable text id for the game within a scan, such as <c>steam:489830</c>; lists and results are matched by it.</param>
/// <param name="Source">The store the game was found in.</param>
/// <param name="StoreId">The game's id in that store (Steam app id or GOG product id), as text.</param>
/// <param name="Name">The name to show: the template's when one matched, the store's otherwise.</param>
/// <param name="InstallPath">The game's install folder as an absolute path on this machine.</param>
/// <param name="TemplateId">The id of the template that matched, or null when none did.</param>
/// <param name="SavesFolder">The saves folder the matched template names, resolved to an absolute path; null when there is no template or it names none.</param>
/// <param name="SavesFolderExists">True when <paramref name="SavesFolder"/> is a folder that exists now; false when it is missing or there is none.</param>
/// <param name="Draft">The definition <see cref="IGamesApi.AddLibraryGameAsync"/> would add, with a fresh id and the install folder filled in.</param>
/// <param name="AddedGameId">The id of the game already in the library that this one is, or null when it is not added yet.</param>
public sealed record LibraryGame(
    string Key,
    LibrarySource Source,
    string StoreId,
    string Name,
    string InstallPath,
    string? TemplateId,
    string? SavesFolder,
    bool SavesFolderExists,
    GameDefinition Draft,
    string? AddedGameId)
{
    /// <summary>True when the game is already in the library.</summary>
    public bool AlreadyAdded => AddedGameId is not null;
}

/// <summary>What a scan of the store libraries found.</summary>
/// <param name="Games">Every installed game found, in store order and then by name; empty when none were found.</param>
/// <param name="SteamFound">True when the Steam client was found on this computer.</param>
/// <param name="GogFound">True when GOG Galaxy's install records could be read on this computer.</param>
public sealed record LibraryScan(IReadOnlyList<LibraryGame> Games, bool SteamFound, bool GogFound);
