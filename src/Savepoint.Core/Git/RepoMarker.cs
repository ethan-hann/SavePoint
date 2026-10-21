using Savepoint.Core.Errors;
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
        ConsentMarkerState.Unreadable unreadable => throw Unreadable(repo, unreadable.Error),
        _ => MarkerState.Corrupt,
    };

    public static RepoConsent Consent(RepoRef repo) =>
        ConsentMarker.Read(repo.WorkTree) is ConsentMarkerState.Valid valid && Matches(valid.Marker, repo)
            ? new RepoConsent(true, valid.Marker.ConfirmedAt)
            : new RepoConsent(false, null);

    // Throws ConsentMarkerException: missing, another game or role, or a file that is not a marker.
    public static void Verify(RepoRef repo) => Translate(repo, () => ConsentMarker.Verify(repo.WorkTree, repo.GameId, repo.Role));

    // Writes the marker; refuses another game's marker, and a corrupt one unless asked to replace it.
    public static void Write(RepoRef repo, DateTimeOffset confirmedAt, bool replaceCorrupt) =>
        Translate(repo, () => ConsentMarker.Write(repo.WorkTree, repo.GameId, repo.Role, confirmedAt, replaceCorrupt));

    // Removes the marker only when it is ours; anything else is left exactly as it is.
    public static void RemoveIfOurs(RepoRef repo)
    {
        if (Assess(repo) == MarkerState.Ours)
        {
            Translate(repo, () => ConsentMarker.Remove(repo.WorkTree, repo.GameId, repo.Role));
        }
    }

    private static bool Matches(ConsentMarkerData marker, RepoRef repo) =>
        string.Equals(marker.GameId, repo.GameId, StringComparison.Ordinal) && marker.Role == repo.Role;

    // The marker is a file in the user's folder; one that cannot be read or written is reported as a failed operation on that path.
    private static void Translate(RepoRef repo, Action action)
    {
        try
        {
            action();
        }
        catch (IOException e)
        {
            throw Unreadable(repo, e);
        }
    }

    private static GitOperationException Unreadable(RepoRef repo, Exception cause) =>
        new(Path.Combine(repo.WorkTree, ConsentMarker.FileName), $"The consent marker in '{repo.WorkTree}' could not be read or written: {cause.Message}");
}

internal enum MarkerState
{
    Missing,
    Ours,
    Foreign,
    Corrupt,
}
