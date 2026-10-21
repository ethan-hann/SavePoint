namespace Savepoint.Core.Errors;

/// <summary>
/// The profile is the active one (both folders hold its latest points), which blocks deleting it
/// (<c>error.gameService.profileActive</c>). Args: <c>profileId</c>.
/// </summary>
public sealed class ProfileActiveException : SavepointException
{
    /// <summary>Creates the error.</summary>
    /// <param name="profileId">The id of the active profile.</param>
    public ProfileActiveException(string profileId)
        : base(
            "error.gameService.profileActive",
            $"Profile '{profileId}' is the active profile.",
            new Dictionary<string, object?> { ["profileId"] = profileId })
    {
    }
}
