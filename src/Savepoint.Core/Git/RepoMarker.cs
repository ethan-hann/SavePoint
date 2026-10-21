using Savepoint.Core.Catalog;
using Savepoint.Core.Paths;

namespace Savepoint.Core.Git;

// Every use of the consent marker by the engine goes through here, so the marker API is touched in one place.
internal static class RepoMarker
{
    public static MarkerState Assess(RepoRef repo) => ConsentMarker.Read(repo.WorkTree) switch
    {
        ConsentMarkerState.Missing => MarkerState.Missing,
        ConsentMarkerState.Valid valid when Matches(valid.Marker, repo) => MarkerState.Ours,
        ConsentMarkerState.Valid => MarkerState.Foreign,
        _ => MarkerState.Corrupt,
    };

    public static RepoConsent Consent(RepoRef repo) =>
        ConsentMarker.Read(repo.WorkTree) is ConsentMarkerState.Valid valid && Matches(valid.Marker, repo)
            ? new RepoConsent(true, valid.Marker.ConfirmedAt)
            : new RepoConsent(false, null);

    // Throws ConsentMarkerException: missing, another game or role, or a file that is not a marker.
    public static void Verify(RepoRef repo) => ConsentMarker.Verify(repo.WorkTree, repo.GameId, ToMarkerRole(repo.Role));

    public static void Write(RepoRef repo, DateTimeOffset confirmedAt) =>
        ConsentMarker.Write(repo.WorkTree, repo.GameId, ToMarkerRole(repo.Role), confirmedAt);

    // Removes the marker only when it is ours; anything else is left exactly as it is.
    public static void RemoveIfOurs(RepoRef repo)
    {
        if (Assess(repo) == MarkerState.Ours)
        {
            ConsentMarker.Remove(repo.WorkTree, repo.GameId, ToMarkerRole(repo.Role));
        }
    }

    private static bool Matches(ConsentMarkerData marker, RepoRef repo) =>
        string.Equals(marker.GameId, repo.GameId, StringComparison.Ordinal) && marker.Role == ToMarkerRole(repo.Role);

    private static ConsentRole ToMarkerRole(RepoRole role) => role == RepoRole.Install ? ConsentRole.Install : ConsentRole.External;
}

internal enum MarkerState
{
    Missing,
    Ours,
    Foreign,
    Corrupt,
}
