using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Paths;
using Savepoint.Core.Platform;

namespace Savepoint.Core.Library;

// Finds the installed games of both stores, matches them to templates, and works out what adding each would save.
internal static class LibraryScanner
{
    /// <summary>
    /// Reads both stores and matches every installed game to a template. Takes no lock and does not look at the library's own games,
    /// so a slow drive holds nothing up; <see cref="Annotate"/> adds which games are already in the library.
    /// </summary>
    public static LibraryScan Scan(LibrarySources sources, IPlatform platform, PathTokenResolver tokens, CancellationToken cancellationToken)
    {
        var installed = new List<InstalledGame>();

        var steamFolder = sources.Steam.FindSteamFolder();
        if (steamFolder is not null)
        {
            installed.AddRange(SteamInstalledGames.Scan(steamFolder, PathSyntax.For(platform.Os), cancellationToken));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var records = sources.Gog?.ReadInstalls();
        if (records is not null)
        {
            installed.AddRange(GogInstalledGames.From(records, cancellationToken));
        }

        var syntax = PathSyntax.For(platform.Os);
        var matched = DropContainers(installed, syntax).Select(game => (Game: game, Template: TemplateMatcher.Find(sources.Templates, game)));
        var games = new List<LibraryGame>(installed.Count);
        foreach (var (game, template) in OneForEachFolder(matched, syntax))
        {
            cancellationToken.ThrowIfCancellationRequested();
            games.Add(Describe(game, template, platform, tokens, null));
        }

        return new LibraryScan(games, steamFolder is not null, records is not null);
    }

    /// <summary>Marks the games of a scan that are already in the library. This is the only part that needs the path judge.</summary>
    public static LibraryScan Annotate(LibraryScan scan, IReadOnlyList<GameDefinition> existing, PathTokenResolver tokens, PathSafety safety, CancellationToken cancellationToken)
    {
        var resolved = ResolveExisting(existing, tokens);
        var games = new List<LibraryGame>(scan.Games.Count);
        foreach (var game in scan.Games)
        {
            cancellationToken.ThrowIfCancellationRequested();
            games.Add(game with { AddedGameId = FindAdded(game.InstallPath, game.SavesFolder, SteamIdOf(game), resolved, safety) });
        }

        return scan with { Games = games };
    }

    /// <summary>The Steam app id the "already added" check uses: the store's own id for a Steam game, none for any other.</summary>
    public static int? SteamIdOf(LibraryGame game) =>
        game.Source == LibrarySource.Steam && int.TryParse(game.StoreId, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id) ? id : null;

    /// <summary>A game of the library with its folders resolved once, so each installed game is not checked against paths resolved again.</summary>
    public sealed record ExistingGame(string Id, int? SteamAppId, string? Install, string? Saves);

    /// <summary>Resolves every game's folders once.</summary>
    public static IReadOnlyList<ExistingGame> ResolveExisting(IReadOnlyList<GameDefinition> existing, PathTokenResolver tokens) =>
        [.. existing.Select(game => new ExistingGame(game.Id, game.SteamAppId, TryResolve(tokens, game.Install.Path), game.External is { } saves ? TryResolve(tokens, saves.Path) : null))];

    /// <summary>
    /// The id of the game that this install folder, saves folder or Steam app id already belongs to; null when none. A saves folder
    /// another game tracks counts, because adding a second game over it would be refused.
    /// </summary>
    public static string? FindAdded(string installPath, string? savesFolder, int? steamAppId, IReadOnlyList<ExistingGame> existing, PathSafety safety)
    {
        foreach (var other in existing)
        {
            if (steamAppId is { } id && other.SteamAppId == id)
            {
                return other.Id;
            }

            if (other.Install is { } theirs && Overlaps(safety, installPath, theirs))
            {
                return other.Id;
            }

            if (savesFolder is not null && other.Saves is { } theirSaves && Overlaps(safety, savesFolder, theirSaves))
            {
                return other.Id;
            }
        }

        return null;
    }

    /// <summary>
    /// The launch a game is added with: the template's program when it can run here (on Windows, and found in the install folder), and
    /// the store's own way to start the game otherwise.
    /// </summary>
    public static LaunchConfig LaunchFor(GameTemplate template, InstalledGame game, OsKind os)
    {
        var launch = template.Definition.Launch;
        if (launch.Method != LaunchMethod.Exe)
        {
            return launch;
        }

        var runs = os == OsKind.Windows && launch.ExePath is { Length: > 0 } exe && File.Exists(Path.Combine(game.InstallPath, exe));
        return runs ? launch : game.Launch;
    }

    // A folder that holds another installed game's folder is a place for games, not a game, and tracking it would track them all.
    private static List<InstalledGame> DropContainers(List<InstalledGame> installed, PathSyntax syntax)
    {
        var comparison = syntax.Comparison;
        var separator = syntax.Separator;
        static string Trim(string path, char separator) => path.TrimEnd(separator, '/', '\\');
        return [.. installed.Where(game =>
        {
            var parent = Trim(game.InstallPath, separator) + separator;
            return !installed.Any(other => Trim(other.InstallPath, separator).StartsWith(parent, comparison));
        })];
    }

    // Apps can share a folder (a game and its editor); a folder is added once, as the app a template describes, else the lowest id.
    private static IEnumerable<(InstalledGame Game, GameTemplate? Template)> OneForEachFolder(IEnumerable<(InstalledGame Game, GameTemplate? Template)> matched, PathSyntax syntax) =>
        matched
            .GroupBy(pair => pair.Game.InstallPath, StringComparer.FromComparison(syntax.Comparison))
            .Select(group => group
                .OrderBy(pair => pair.Template is null)
                .ThenBy(pair => pair.Game.SteamAppId ?? int.MaxValue)
                .ThenBy(pair => pair.Game.StoreId, StringComparer.Ordinal)
                .First())
            .OrderBy(pair => pair.Game.Source)
            .ThenBy(pair => pair.Game.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(pair => pair.Game.StoreId, StringComparer.Ordinal);

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

    // A game with no template is added with its install folder, a way to start it, and the install-only rules every new game leaves logs out with.
    private static GameDefinition Plain(InstalledGame game) => new(
        Id: CatalogIds.NewGameId(game.Name),
        Name: game.Name,
        Install: new InstallLocation(game.InstallPath),
        Launch: game.Launch,
        SteamAppId: game.SteamAppId,
        InstallIgnorePaths: RunTimeOutput.DefaultRules);

    private static GameDefinition FromTemplate(GameTemplate template, InstalledGame game, IPlatform platform) =>
        template.ToDraft(platform.Os) with
        {
            Install = new InstallLocation(game.InstallPath),
            Launch = LaunchFor(template, game, platform.Os),
            // A Steam game keeps the id it is installed under; a game from another store takes the template's, which only picks artwork.
            SteamAppId = game.SteamAppId ?? template.Definition.SteamAppId,
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
