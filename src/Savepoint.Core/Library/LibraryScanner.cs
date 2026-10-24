using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Paths;
using Savepoint.Core.Platform;

namespace Savepoint.Core.Library;

// Finds the installed games of both stores, matches them to templates, and works out what adding each would save.
internal static class LibraryScanner
{
    public static LibraryScan Scan(
        LibrarySources sources,
        IPlatform platform,
        PathTokenResolver tokens,
        PathSafety safety,
        IReadOnlyList<GameDefinition> existing,
        CancellationToken cancellationToken)
    {
        var installed = new List<InstalledGame>();

        var steamFolder = sources.Steam.FindSteamFolder();
        if (steamFolder is not null)
        {
            installed.AddRange(SteamInstalledGames.Scan(steamFolder, PathSyntax.For(platform.Os)));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var records = sources.Gog?.ReadInstalls();
        if (records is not null)
        {
            installed.AddRange(GogInstalledGames.From(records));
        }

        var games = new List<LibraryGame>(installed.Count);
        foreach (var game in installed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var template = TemplateMatcher.Find(sources.Templates, game);
            games.Add(Describe(game, template, platform, tokens, FindAdded(game.InstallPath, game.SteamAppId, existing, tokens, safety)));
        }

        return new LibraryScan(games, steamFolder is not null, records is not null);
    }

    /// <summary>The id of the game in <paramref name="existing"/> that this install folder or Steam app id already belongs to; null when none.</summary>
    public static string? FindAdded(string installPath, int? steamAppId, IReadOnlyList<GameDefinition> existing, PathTokenResolver tokens, PathSafety safety)
    {
        foreach (var other in existing)
        {
            if (steamAppId is { } id && other.SteamAppId == id)
            {
                return other.Id;
            }

            if (TryResolve(tokens, other.Install.Path) is { } theirs && Overlaps(safety, installPath, theirs))
            {
                return other.Id;
            }
        }

        return null;
    }

    private static LibraryGame Describe(InstalledGame game, GameTemplate? template, IPlatform platform, PathTokenResolver tokens, string? addedGameId)
    {
        var draft = template is null ? Plain(game) : FromTemplate(template, game, platform);
        var saves = draft.External is { } external && template is not null ? TryResolve(tokens, external.Path) : null;
        return new LibraryGame(
            Key: KeyOf(game),
            game.Source,
            game.StoreId,
            draft.Name,
            game.InstallPath,
            template?.Id,
            saves,
            saves is not null && Directory.Exists(saves),
            draft,
            addedGameId);
    }

    private static string KeyOf(InstalledGame game) => $"{(game.Source == LibrarySource.Steam ? "steam" : "gog")}:{game.StoreId}";

    // A game with no template is added with its install folder and a way to start it, and nothing else.
    private static GameDefinition Plain(InstalledGame game) => new(
        Id: CatalogIds.NewGameId(game.Name),
        Name: game.Name,
        Install: new InstallLocation(game.InstallPath),
        Launch: game.Launch,
        SteamAppId: game.SteamAppId);

    private static GameDefinition FromTemplate(GameTemplate template, InstalledGame game, IPlatform platform) =>
        template.ToDraft(platform.Os) with
        {
            Install = new InstallLocation(game.InstallPath),
            SteamAppId = template.Definition.SteamAppId ?? game.SteamAppId,
        };

    private static string? TryResolve(PathTokenResolver tokens, string raw)
    {
        try
        {
            return string.IsNullOrWhiteSpace(raw) ? null : tokens.Resolve(raw);
        }
        catch (ValidationException)
        {
            return null;
        }
    }

    // A path the judge cannot read counts as no overlap: the add itself checks again and refuses with a reason.
    private static bool Overlaps(PathSafety safety, string a, string b)
    {
        try
        {
            return safety.PathsOverlap(a, b);
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
