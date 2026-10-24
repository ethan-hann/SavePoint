using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.Core.Library;

// Picks the template that describes an installed game: the store's id first, then the install folder's name, ignoring case.
internal static class TemplateMatcher
{
    public static GameTemplate? Find(IReadOnlyList<GameTemplate> templates, InstalledGame game) =>
        templates.FirstOrDefault(template => MatchesId(template, game))
        ?? templates.FirstOrDefault(template => MatchesFolder(template, game));

    private static bool MatchesId(GameTemplate template, InstalledGame game) => game.Source switch
    {
        LibrarySource.Steam => game.SteamAppId is { } appId
            && (template.Definition.SteamAppId == appId || template.Hints.SteamAppIds.Contains(appId)),
        LibrarySource.Gog => template.Hints.GogIds.Any(id => string.Equals(id.Trim(), game.StoreId, StringComparison.Ordinal)),
        _ => false,
    };

    private static bool MatchesFolder(GameTemplate template, InstalledGame game)
    {
        var names = game.Source == LibrarySource.Steam ? template.Hints.SteamInstallDirs : template.Hints.GogFolders;
        return names.Any(name => string.Equals(name.Trim(), game.FolderName, StringComparison.OrdinalIgnoreCase));
    }
}
