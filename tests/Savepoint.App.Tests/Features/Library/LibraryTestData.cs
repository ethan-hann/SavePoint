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
