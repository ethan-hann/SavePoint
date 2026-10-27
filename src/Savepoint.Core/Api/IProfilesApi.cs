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
    /// <para>
    /// A folder named in <paramref name="startEmpty"/> gets a history that begins with an empty snapshot instead of a snapshot of its
    /// files, as a duplicate's saves do. No file on disk is touched: the folder keeps its files until the player switches to the
    /// profile, which the launch check announces and asks about. A retry of a folder that failed to start empty starts it empty again.
    /// </para>
    /// <para>
    /// A folder may start empty only when no file in it can be lost (D65.2). That holds when the folder does not exist, or when it sits on
    /// the branch of one of the game's profiles (not on an older point), that branch has a snapshot, no switch of the folder stopped part
    /// way, no file in it is new or changed since that snapshot, and every file could be read. Files the game leaves out do not count as
    /// changes: no snapshot holds them, and a switch never removes them. Otherwise the files would stay behind as the new profile's new
    /// changes, and discarding them at launch would delete the only copy. A game's first profile therefore cannot start a folder empty
    /// unless the folder does not exist. The check runs before a job is queued and again under the game's lock, just before the folder is
    /// started; a refusal there fails that folder, and a retry checks again. A folder that does not exist when the job runs fails with
    /// <c>error.path.missing</c> like any other missing folder.
    /// </para>
    /// </remarks>
    /// <param name="gameId">The game.</param>
    /// <param name="name">The profile's name. Required.</param>
    /// <param name="notes">The profile's notes, or null.</param>
    /// <param name="pointName">The name of the first history point.</param>
    /// <param name="startEmpty">
    /// The folders that start with nothing; null or empty for none. Only <see cref="RepoRole.External"/> (saves) and
    /// <see cref="RepoRole.Mods"/> may be named, and only when the game has that folder: anything else fails with
    /// <c>error.validation.invalidPayload</c> (issue <see cref="ValidationMessageIds.ProfileStartEmptyInvalid"/> at <c>startEmpty</c>) before a job is queued,
    /// since the game's own files are what a profile is for (D29.3). A folder whose files no snapshot holds (a new or changed file, or a
    /// folder SavePoint has no marker in) fails the same way with <see cref="ValidationMessageIds.ProfileStartEmptyUnsaved"/>, and a folder
    /// that cannot be checked (a file held open or unreadable, a stopped switch, or a folder on an older point) with
    /// <see cref="ValidationMessageIds.ProfileStartEmptyUnchecked"/>, which a snapshot does not fix.
    /// </param>
    Task<OperationResult<JobStarted>> CreateAsync(string gameId, string name, string? notes, string pointName, IReadOnlyCollection<RepoRole>? startEmpty = null);

    /// <summary>Changes the fields a user edits. Fails with <c>error.validation.invalidPayload</c> when the name is empty.</summary>
    /// <param name="gameId">The game.</param>
    /// <param name="profileId">The profile to change.</param>
    /// <param name="changes">The new values.</param>
    /// <returns>The profile as stored.</returns>
    Task<OperationResult<Profile>> UpdateAsync(string gameId, string profileId, ProfileChanges changes);

    /// <summary>
    /// Makes a new profile that starts from another profile's history. Takes the game's lock (D13.14). It finishes in every folder or
    /// changes nothing: a failure takes back the branch of every folder that got one.
    /// </summary>
    /// <param name="gameId">The game.</param>
    /// <param name="sourceProfileId">The profile to copy.</param>
    /// <param name="name">The new profile's name.</param>
    /// <returns>The new profile.</returns>
    Task<OperationResult<Profile>> DuplicateAsync(string gameId, string sourceProfileId, string name);

    /// <summary>
    /// Brings a recently removed snapshot back as a new profile. The snapshot's folders become the new profile's history: in each folder
    /// its branch starts at the commit the snapshot recorded there, or, for a folder the snapshot did not record, at the latest commit the
    /// profile it came from had at or before it, counting the commits that were taken off that profile (the rule History and "Go back to
    /// this" use). The profile it came from is not touched,
    /// and no file in a game folder is. Takes the game's lock and finishes in every folder or changes nothing, like
    /// <see cref="DuplicateAsync"/>. The snapshot is part of the new profile from then on, so it leaves the recently removed list; its
    /// record stays until the player frees up space. When the snapshot was recorded in one folder only and a profile still holds the
    /// other folder's commit under the same snapshot id, the recorded folder's commit comes back as a copy under a new id (same files,
    /// name, time and dates) and the record moves to the copy, so History lists the new profile's point as its own and the other profile's
    /// point does not change; the snapshot's id in that case is the copy's, and it is the one listed if the new profile is deleted later.
    /// </summary>
    /// <remarks>
    /// Fails with <c>error.removed.notFound</c> when the snapshot is not in the list any more (the player freed up space, or it was
    /// brought back), and with <c>error.removed.notRestorable</c> when it recorded only saves and no install can be found for it, so a
    /// profile made from it could not start. The new profile carries the version override of the profile it came from, when that still exists.
    /// </remarks>
    /// <param name="gameId">The game.</param>
    /// <param name="snapshotId">The snapshot, from <see cref="IHistoryApi.GetRecentlyRemovedAsync"/>.</param>
    /// <param name="name">The new profile's name. Required.</param>
    /// <returns>The new profile.</returns>
    Task<OperationResult<Profile>> BringBackAsync(string gameId, HistoryPointId snapshotId, string name);

    /// <summary>
    /// Counts the history points only this profile holds, in total and per folder, and says whether the delete would be refused
    /// (D17).
    /// </summary>
    /// <param name="gameId">The game.</param>
    /// <param name="profileId">The profile that might be deleted.</param>
    /// <param name="cancellationToken">Cancelling fails the call with <c>error.operation.cancelled</c> (D23).</param>
    Task<OperationResult<ProfileDeletePreview>> GetDeletePreviewAsync(string gameId, string profileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a profile and the history only it holds; storage optimization reclaims the space (D28). Every folder is
    /// checked before any is touched (D13.9), and a failure in a later one puts the earlier ones back, so a failed delete changes
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
