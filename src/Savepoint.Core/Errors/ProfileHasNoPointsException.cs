namespace Savepoint.Core.Errors;

/// <summary>The profile has no saved point yet, so there is nothing to switch to or move. Nothing was changed.</summary>
public sealed class ProfileHasNoPointsException : SavepointException
{
    /// <summary>Builds the error for a profile without a saved point.</summary>
    /// <param name="profileId">The profile that is empty or does not exist.</param>
    public ProfileHasNoPointsException(string profileId)
        : base(
            "error.profile.noPoints",
            $"Profile '{profileId}' has no saved points.",
            new Dictionary<string, object?> { ["profileId"] = profileId })
    {
    }
}
