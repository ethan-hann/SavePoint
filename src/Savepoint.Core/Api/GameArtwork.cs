namespace Savepoint.Core.Api;

/// <summary>
/// The artwork a game has right now: where each of its two images is on disk and where they came from.
/// </summary>
/// <param name="GameId">The game.</param>
/// <param name="Source">Which source the paths come from. <see cref="ArtworkSource.None"/> has no paths.</param>
/// <param name="WidePath">
/// Absolute path of the wide image for the library card, or null when there is none. The user's own image is used for every shape,
/// so for <see cref="ArtworkSource.Custom"/> it equals <paramref name="SquarePath"/>.
/// </param>
/// <param name="SquarePath">
/// Absolute path of the tall image for the square tiles, or null when there is none. A Steam game can have one of the two images and
/// not the other.
/// </param>
/// <param name="SteamAppId">
/// The Steam app id the lookup uses: the one stored on the game, else the one found from its launch link or folders; null when
/// there is none. It is reported whatever the source, so the editor can show it.
/// </param>
/// <param name="SteamAppIdDetected">True when <paramref name="SteamAppId"/> was found rather than stored on the game.</param>
public sealed record GameArtwork(
    string GameId,
    ArtworkSource Source,
    string? WidePath = null,
    string? SquarePath = null,
    int? SteamAppId = null,
    bool SteamAppIdDetected = false)
{
    /// <summary>A game with no artwork and no Steam app id.</summary>
    /// <param name="gameId">The game.</param>
    public static GameArtwork None(string gameId) => new(gameId, ArtworkSource.None);
}
