namespace Savepoint.Core.Api;

/// <summary>
/// A game's artwork (D30): the user's own image, else images from Steam, else nothing, in which case the app draws the game's name
/// tint. It is separate from <see cref="IGamesApi"/> so the game summaries stay as they are.
/// </summary>
/// <remarks>
/// See <see cref="OperationResult{T}"/> for the rules every member follows. Every member fails with <c>error.game.notFound</c> for a
/// game the catalog does not hold.
/// </remarks>
public interface IArtworkApi
{
    /// <summary>
    /// Raised after a game's artwork changed, with the game's id. It is raised on a background thread, so the app marshals it to the
    /// UI thread. A lookup that finds nothing new raises nothing.
    /// </summary>
    event EventHandler<ArtworkChange>? ArtworkChanged;

    /// <summary>
    /// Reads the artwork a game has now. It only reads files, so it is quick and never waits for the network. When the game has no
    /// image of its own and no Steam image is on hand, it also starts a lookup in the background unless one is not due (a miss
    /// within the last seven days, or one already made this session); the result arrives through <see cref="ArtworkChanged"/>. The
    /// lookup tries the Steam client's own cache, then Steam's servers when <c>LookUpArtworkOnline</c> is on and a handler was
    /// supplied. Steam images kept for a different app id than the game has now are discarded.
    /// </summary>
    /// <param name="gameId">The game.</param>
    /// <param name="cancellationToken">Cancelling fails the call with <c>error.operation.cancelled</c> (D23).</param>
    Task<OperationResult<GameArtwork>> GetAsync(string gameId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes a copy of an image file the game's own artwork, replacing any it had. The file is copied in; the original is never
    /// referenced again. Fails with <c>error.artwork.notAnImage</c> when the file is missing or is not a whole PNG, JPEG, or WebP
    /// image, and with <c>error.artwork.tooLarge</c> above 25 MB; nothing changes then.
    /// </summary>
    /// <param name="gameId">The game.</param>
    /// <param name="imagePath">The file the user picked.</param>
    /// <returns>The game's artwork after the change.</returns>
    Task<OperationResult<GameArtwork>> SetCustomImageAsync(string gameId, string imagePath);

    /// <summary>Removes the game's own image, if it has one, so Steam's images or the tint show again.</summary>
    /// <param name="gameId">The game.</param>
    /// <returns>The game's artwork after the change.</returns>
    Task<OperationResult<GameArtwork>> ClearCustomImageAsync(string gameId);

    /// <summary>
    /// Looks for Steam images again right now, ignoring any remembered miss, and answers when the lookup has finished. It finds the
    /// app id again, tries the Steam client's cache, then Steam's servers when allowed. Images already found are kept when nothing
    /// better turns up.
    /// </summary>
    /// <param name="gameId">The game.</param>
    /// <returns>The game's artwork after the lookup.</returns>
    Task<OperationResult<GameArtwork>> LookAgainAsync(string gameId);
}
