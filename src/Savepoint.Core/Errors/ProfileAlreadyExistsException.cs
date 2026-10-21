namespace Savepoint.Core.Errors;

/// <summary>A profile with that id already exists, so a new one cannot take its name. Nothing was changed.</summary>
public sealed class ProfileAlreadyExistsException : SavepointException
{
    /// <summary>Builds the error for a profile id that is taken.</summary>
    /// <param name="profileId">The id that was asked for.</param>
    public ProfileAlreadyExistsException(string profileId)
        : base(
            "error.profile.alreadyExists",
            $"Profile '{profileId}' already exists.",
            new Dictionary<string, object?> { ["profileId"] = profileId })
    {
    }
}
