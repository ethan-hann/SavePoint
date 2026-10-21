namespace Savepoint.Core.Errors;

public sealed class ProfileActiveException : SavepointException
{
    public ProfileActiveException(string profileId)
        : base(
            "error.gameService.profileActive",
            $"Profile '{profileId}' is the active profile.",
            new Dictionary<string, object?> { ["profileId"] = profileId })
    {
    }
}
