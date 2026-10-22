using Savepoint.Core.Catalog;

namespace Savepoint.Core.Api;

/// <summary>Profiles: the named setups of a game, each a branch of history. Capturing saves a folder's current state into one.</summary>
/// <remarks>See <see cref="OperationResult{T}"/> for the rules every member follows.</remarks>
public interface IProfilesApi
{
    /// <summary>Lists profiles in catalog order. A null game lists every game's profiles, which the rail uses for its counts.</summary>
    /// <param name="gameId">Only this game's profiles; null for every game's.</param>
    /// <param name="cancellationToken">Cancelling fails the call with <c>error.operation.cancelled</c> (D23).</param>
    Task<OperationResult<IReadOnlyList<Profile>>> ListAsync(string? gameId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads what each profile row shows: unsaved changes, counts, version, last played, and whether it is running. One entry per
    /// profile of the game.
    /// </summary>
    /// <param name="gameId">The game.</param>
    /// <param name="cancellationToken">Cancelling fails the call with <c>error.operation.cancelled</c> (D23).</param>
    Task<OperationResult<IReadOnlyList<ProfileRowStats>>> GetRowStatsAsync(string gameId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a profile from what is on disk now, for the new-profile wizard and for "Keep as a new profile" alike. Starts a
    /// capture job and answers at once; its result is a <see cref="CaptureResult"/>.
    /// </summary>
    /// <remarks>
    /// The profile's catalog row appears when the first capture finishes with at least one folder saved (D23). A capture that saves
    /// nothing leaves no row and no branch. A partial result keeps the row and shows the "Only part of the snapshot was taken" card with a
    /// retry for the missing folder.
    /// </remarks>
    /// <param name="gameId">The game.</param>
    /// <param name="name">The profile's name. Required.</param>
    /// <param name="notes">The profile's notes, or null.</param>
    /// <param name="pointName">The name of the first history point.</param>
    Task<OperationResult<JobStarted>> CreateAsync(string gameId, string name, string? notes, string pointName);

    /// <summary>Changes the fields a user edits. Fails with <c>error.validation.invalidPayload</c> when the name is empty.</summary>
    /// <param name="gameId">The game.</param>
    /// <param name="profileId">The profile to change.</param>
    /// <param name="changes">The new values.</param>
    /// <returns>The profile as stored.</returns>
    Task<OperationResult<Profile>> UpdateAsync(string gameId, string profileId, ProfileChanges changes);

    /// <summary>
    /// Makes a new profile that starts from another profile's history. Takes the game's lock (D13.14). It finishes in both folders or
    /// changes nothing: a failure takes back the branch of every folder that got one.
    /// </summary>
    /// <param name="gameId">The game.</param>
    /// <param name="sourceProfileId">The profile to copy.</param>
    /// <param name="name">The new profile's name.</param>
    /// <returns>The new profile.</returns>
    Task<OperationResult<Profile>> DuplicateAsync(string gameId, string sourceProfileId, string name);

    /// <summary>
    /// Counts the history points only this profile holds, in total and per folder, and says whether the delete would be refused
    /// (D17).
    /// </summary>
    /// <param name="gameId">The game.</param>
    /// <param name="profileId">The profile that might be deleted.</param>
    /// <param name="cancellationToken">Cancelling fails the call with <c>error.operation.cancelled</c> (D23).</param>
    Task<OperationResult<ProfileDeletePreview>> GetDeletePreviewAsync(string gameId, string profileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a profile and the history only it holds; storage optimization reclaims the space (D28). Both folders are
    /// checked before either is touched (D13.9), and a failure on the second puts the first back, so a failed delete changes
    /// nothing and can be repeated. Fails with <c>error.gameService.profileActive</c> for the active profile and
    /// <c>error.gameService.gameRunning</c> while the game has an open session (D13.5).
    /// </summary>
    /// <param name="gameId">The game.</param>
    /// <param name="profileId">The profile to delete.</param>
    Task<OperationResult<Unit>> DeleteAsync(string gameId, string profileId);

    /// <summary>
    /// Saves the folders' current state as a new history point on a profile. Starts a capture job and answers at once; its result
    /// is a <see cref="CaptureResult"/>. A null folder list saves every configured folder; passing one folder is how "Retry the
    /// saves" and "Take a snapshot first" work. The job reads the game and the profile again once it holds the game's lock, so it uses the
    /// ignore list as it is by then and fails with <c>error.profile.notFound</c> when the profile was deleted meanwhile.
    /// </summary>
    /// <param name="gameId">The game.</param>
    /// <param name="profileId">The profile that receives the point.</param>
    /// <param name="pointName">The point's name.</param>
    /// <param name="folders">The folders to save; null for all.</param>
    Task<OperationResult<JobStarted>> CaptureAsync(string gameId, string profileId, string pointName, IReadOnlyCollection<RepoRole>? folders = null);
}
