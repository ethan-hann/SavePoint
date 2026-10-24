using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Localization;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Library;

internal static class LibraryTestData
{
    public static UiFormatter Text { get; } = new(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), os: "windows", timeZone: TimeZoneInfo.Utc);

    // A game as a scan reports it. A game with a saves folder has a template; one without has none.
    public static LibraryGame Game(
        string name,
        LibrarySource source = LibrarySource.Steam,
        string? id = null,
        string? saves = null,
        bool savesExist = true,
        string? addedGameId = null)
    {
        var storeId = id ?? (Math.Abs(name.GetHashCode(StringComparison.Ordinal)) % 100000).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var install = $@"C:\Games\{name}";
        var key = $"{(source == LibrarySource.Steam ? "steam" : "gog")}:{storeId}";
        var draft = new GameDefinition(
            Id: "id-" + storeId,
            Name: name,
            Install: new InstallLocation(install),
            External: saves is null ? null : new ExternalLocation(saves),
            Launch: new LaunchConfig(LaunchMethod.Uri, Uri: "steam://rungameid/" + storeId));
        return new LibraryGame(key, source, storeId, name, install, saves is null ? null : "template", saves, saves is not null && savesExist, draft, addedGameId);
    }

    public static LibraryScan Scan(params LibraryGame[] games) => new(games, true, true);
}

// A believable library for screens: a few real names with and without saves folders, or 200 games for a list that has to scroll.
internal static class LibraryStates
{
    public static LibraryScan Scan(bool many)
    {
        var games = new List<LibraryGame>
        {
            LibraryTestData.Game("The Elder Scrolls V: Skyrim Special Edition", id: "489830", saves: @"C:\Users\you\Documents\My Games\Skyrim Special Edition\Saves"),
            LibraryTestData.Game("Counter-Strike: Source", id: "240"),
            LibraryTestData.Game("Cyberpunk 2077", LibrarySource.Gog, "1423049311", saves: @"C:\Users\you\Saved Games\CD Projekt Red\Cyberpunk 2077", savesExist: false),
            LibraryTestData.Game("Deus Ex GOTY", LibrarySource.Gog, "1207658995"),
            LibraryTestData.Game("Portal 2", id: "620", addedGameId: "portal-2-ab12"),
        };
        if (many)
        {
            games.AddRange(Enumerable.Range(1, 195).Select(n => LibraryTestData.Game($"Sample Game {n}", id: (10000 + n).ToString(System.Globalization.CultureInfo.InvariantCulture))));
        }

        return LibraryTestData.Scan([.. games]);
    }
}
