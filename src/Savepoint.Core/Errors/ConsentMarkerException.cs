namespace Savepoint.Core.Errors;

public sealed class ConsentMarkerException : SavepointException
{
    public ConsentMarkerException(
        ConsentFailure reason,
        string path,
        string role,
        string? existingGameId,
        string? existingRole)
        : base(
            reason == ConsentFailure.Missing ? "error.consentMarker.missing" : "error.consentMarker.conflict",
            reason == ConsentFailure.Missing
                ? $"The {role} folder '{path}' has no consent marker."
                : $"The {role} folder '{path}' is claimed by game '{existingGameId}' as its {existingRole} folder.",
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
}
