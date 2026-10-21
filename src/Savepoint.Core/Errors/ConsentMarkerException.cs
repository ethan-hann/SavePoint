using Savepoint.Core.Catalog;

namespace Savepoint.Core.Errors;

public sealed class ConsentMarkerException : SavepointException
{
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
