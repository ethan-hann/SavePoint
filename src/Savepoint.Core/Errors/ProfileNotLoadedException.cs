using Savepoint.Core.Catalog;

namespace Savepoint.Core.Errors;

/// <summary>
/// A snapshot was asked for on a profile that a game folder does not hold now (<c>error.profile.notLoaded</c>). A snapshot copies what the
/// folders hold, which is the loaded profile's files, so taking one on another profile would put another profile's files into it. Nothing
/// was written. Args: <c>profileId</c> (the profile; the branch name when it is the engine that refuses) and <c>role</c> (the folder that
/// holds something else).
/// </summary>
public sealed class ProfileNotLoadedException : SavepointException
{
    /// <summary>Creates the error.</summary>
    /// <param name="profileId">The profile a snapshot was asked for; the engine, which knows only branches, gives the branch name.</param>
    /// <param name="role">The folder that holds another profile.</param>
    public ProfileNotLoadedException(string profileId, RepoRole role)
        : base(
            "error.profile.notLoaded",
            $"The {role} folder does not hold profile '{profileId}', so a snapshot of what it holds cannot be added to that profile.",
            new Dictionary<string, object?> { ["profileId"] = profileId, ["role"] = role })
    {
    }
}
