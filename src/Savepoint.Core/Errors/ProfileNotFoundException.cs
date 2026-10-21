namespace Savepoint.Core.Errors;

/// <summary>
/// A call named a profile the game does not have, whether it never existed, was deleted, or belongs to another game
/// (<c>error.profile.notFound</c>). Args: <c>gameId</c>, <c>profileId</c>.
/// </summary>
public sealed class ProfileNotFoundException : SavepointException
{
    /// <summary>Creates the error.</summary>
    /// <param name="gameId">The game the call was for.</param>
    /// <param name="profileId">The id that matched none of that game's profiles.</param>
    public ProfileNotFoundException(string gameId, string profileId)
        : base(
            "error.profile.notFound",
            $"Game '{gameId}' has no profile '{profileId}'.",
            new Dictionary<string, object?> { ["gameId"] = gameId, ["profileId"] = profileId })
    {
    }
}
