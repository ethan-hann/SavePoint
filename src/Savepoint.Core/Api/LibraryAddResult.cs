using Savepoint.Core.Errors;

namespace Savepoint.Core.Api;

/// <summary>How adding one library game ended.</summary>
public enum LibraryAddStatus
{
    /// <summary>The game was added.</summary>
    Added,

    /// <summary>The game was already in the library, so nothing was changed.</summary>
    AlreadyAdded,

    /// <summary>
    /// SavePoint found saved history from a removed game at the game's folders. Nothing was added: the question of bringing it back
    /// is the player's, and is asked when the game is added on its own.
    /// </summary>
    HasOlderHistory,

    /// <summary>The game could not be added; <see cref="LibraryAddResult.Error"/> says why.</summary>
    Refused,
}

/// <summary>The outcome of adding one library game.</summary>
/// <param name="Key">The <see cref="LibraryGame.Key"/> of the game this is about.</param>
/// <param name="Name">The game's name.</param>
/// <param name="Status">How it ended.</param>
/// <param name="GameId">The id of the game in the library when it was added or already there; otherwise null.</param>
/// <param name="Error">Why the game was refused; null for every other status.</param>
/// <param name="SavesFolderLeftOut">
/// For an added game: the saves folder its template names, when it was left out because that folder does not exist yet (a game
/// makes it the first time it is played), so the game was added with its install folder only. Null otherwise.
/// </param>
/// <param name="RulesLeftOff">
/// For an added game: the leave-out rules that now apply to the game's own folder only because they could hide files in its saves
/// folder (or because that folder could not be checked); null when every rule was kept as asked.
/// </param>
public sealed record LibraryAddResult(
    string Key,
    string Name,
    LibraryAddStatus Status,
    string? GameId = null,
    ErrorInfo? Error = null,
    string? SavesFolderLeftOut = null,
    RulesLeftOff? RulesLeftOff = null);
