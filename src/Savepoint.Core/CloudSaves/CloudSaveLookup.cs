using System.Globalization;
using Savepoint.Core.Api;
using Savepoint.Core.Artwork;
using Savepoint.Core.Catalog;

namespace Savepoint.Core.CloudSaves;

/// <summary>
/// Works out which store a game belongs to and asks that store's reader. A null reader stands for a store that is not available on this
/// system, which reads as <see cref="CloudSaveState.Unknown"/>.
/// </summary>
internal sealed class CloudSaveLookup(IStoreCloudReader? steam, IStoreCloudReader? galaxy)
{
    /// <summary>Reads the state for a game. File reads only; a store the game does not clearly belong to gives Unknown.</summary>
    /// <param name="game">The game.</param>
    /// <param name="installPath">The game's install folder with tokens resolved, or null when it has none.</param>
    /// <param name="pathComparison">How this system compares paths.</param>
    public CloudSaveInfo Read(GameDefinition game, string? installPath, StringComparison pathComparison)
    {
        ArgumentNullException.ThrowIfNull(game);

        // The same order the artwork lookup uses: an id the user stored, then the GOG product the installer left, then a Steam id only detected.
        if (game.SteamAppId is { } stored)
        {
            return Ask(steam, stored.ToString(CultureInfo.InvariantCulture), CloudStore.Steam);
        }

        if (GogProductIdDetector.Detect(installPath) is { } product)
        {
            return Ask(galaxy, product, CloudStore.Gog);
        }

        return SteamAppIdDetector.Detect(game, installPath, pathComparison) is { } detected
            ? Ask(steam, detected.ToString(CultureInfo.InvariantCulture), CloudStore.Steam)
            : new CloudSaveInfo(CloudSaveState.Unknown, CloudStore.None);
    }

    private static CloudSaveInfo Ask(IStoreCloudReader? reader, string id, CloudStore store) =>
        new(reader?.Read(id) ?? CloudSaveState.Unknown, store);
}
