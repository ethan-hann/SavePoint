namespace Savepoint.App.Features.Common.Art;

/// <summary>
/// Hands view models each game's <see cref="GameArt"/> by game id. It reads the artwork from the Api only after a tile showing the
/// game is on screen, so no lookup starts before the library has rendered, and it re-reads when the Api says a game's artwork changed.
/// </summary>
public interface IArtworkProvider
{
    /// <summary>The shared artwork of a game: the same instance every time, empty until it has been read.</summary>
    /// <param name="gameId">The game.</param>
    GameArt For(string gameId);

    /// <summary>
    /// A private copy of a game's artwork that the Api never changes, for showing a file the user has chosen before it is saved.
    /// </summary>
    /// <param name="gameId">The game, or an empty id for a game that is not saved yet.</param>
    GameArt Preview(string gameId);

    /// <summary>Reads the artwork of every game that is on screen again, for after a setting that decides what a lookup may do.</summary>
    void Refresh();
}
