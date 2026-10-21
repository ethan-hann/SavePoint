using Savepoint.Core.Catalog;

namespace Savepoint.Core.Errors;

/// <summary>
/// A folder's consent marker does not allow the operation: it is missing (<c>error.consentMarker.missing</c>) or another game or
/// role holds the folder, or the marker file is not one SavePoint wrote (<c>error.consentMarker.conflict</c>). Args: <c>reason</c>,
/// <c>path</c>, <c>role</c>, <c>existingGameId</c>, <c>existingRole</c>; the last two are null for a missing or corrupt marker.
/// </summary>
public sealed class ConsentMarkerException : SavepointException
{
    /// <summary>Creates the error.</summary>
    /// <param name="reason">Whether the marker is missing or in conflict; it picks the message id.</param>
    /// <param name="path">The work tree whose marker was checked.</param>
    /// <param name="role">The role the caller expected the folder to have.</param>
    /// <param name="existingGameId">The game the marker names, or null when there is no readable marker.</param>
    /// <param name="existingRole">The role the marker names, or null when there is no readable marker.</param>
    public ConsentMarkerException(
        ConsentFailure reason,
        string path,
        RepoRole role,
        string? existingGameId,
        RepoRole? existingRole)
        : base(
            reason == ConsentFailure.Missing ? "error.consentMarker.missing" : "error.consentMarker.conflict",
            DevMessage(reason, path, role, existingGameId, existingRole),
            new Dictionary<string, object?>
            {
                ["reason"] = reason,
                ["path"] = path,
                ["role"] = role,
                ["existingGameId"] = existingGameId,
                ["existingRole"] = existingRole,
            })
    {
    }

    private static string DevMessage(ConsentFailure reason, string path, RepoRole role, string? existingGameId, RepoRole? existingRole)
    {
        var folder = $"The {ErrorArgs.NameOf(role)} folder '{path}'";
        if (reason == ConsentFailure.Missing)
        {
            return $"{folder} has no consent marker.";
        }

        var claimedAs = existingRole is { } known ? $" as its {ErrorArgs.NameOf(known)} folder" : string.Empty;
        return $"{folder} is claimed by game '{existingGameId}'{claimedAs}.";
    }
}
