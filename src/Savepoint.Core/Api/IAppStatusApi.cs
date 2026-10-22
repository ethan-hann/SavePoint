namespace Savepoint.Core.Api;

/// <summary>The startup checks (D18), the recovery of corrupt catalog files, and the two ways on when the storage folder is gone (D37).</summary>
/// <remarks>See <see cref="OperationResult{T}"/> for the rules every member follows.</remarks>
public interface IAppStatusApi
{
    /// <summary>
    /// Checks that the bundled git runs, that every catalog file can be read, and that the storage folder can be reached. A problem
    /// found is part of the answer, not a failure of the call.
    /// </summary>
    /// <param name="cancellationToken">Cancelling fails the call with <c>error.operation.cancelled</c> (D23).</param>
    Task<OperationResult<StartupStatus>> CheckAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Fixes one corrupt catalog file by restoring its last good copy or starting it empty. Either way the corrupt file is moved
    /// aside, never deleted. A restore copies the backup next to the file before anything moves, so a copy that fails leaves the
    /// corrupt file where it was. Fails when the path is not one of the corrupt files, or when a backup is asked for and there is none.
    /// </summary>
    /// <param name="path">A path from <see cref="CatalogStatus.CorruptFiles"/>.</param>
    /// <param name="recovery">Which fix to apply.</param>
    /// <param name="cancellationToken">Cancelling fails the call with <c>error.operation.cancelled</c> (D23).</param>
    /// <returns>Where the corrupt file was moved, so the recovery screen can say so. Check again afterwards.</returns>
    Task<OperationResult<string>> RecoverCatalogFileAsync(string path, CatalogRecovery recovery, CancellationToken cancellationToken = default);

    /// <summary>
    /// "Find the folder": points SavePoint at the folder that holds its saved history now, after the startup check found the storage
    /// folder unreachable. Both startup choices are refused while the saved folder can be reached. The chosen folder is judged before
    /// anything changes: it has to exist and hold SavePoint's own history for at least one of the games in the catalog (for any game
    /// folder SavePoint made, when the catalog lists none), and it has to pass the checks a storage move makes on its destination, so
    /// it is not inside the folder the installer owns, does not overlap one of a game's own folders, and is not what an unfinished
    /// move left behind. Each git directory in it is then made safe to use: its config is rewritten to the settings SavePoint writes
    /// and its hooks folder is emptied, so nothing the folder carries can run a program. Last, every listed game gets its repositories
    /// in the folder, an empty one for each of its folders that has none there and whose consent marker names the game; a folder that
    /// cannot take one (it is gone, the path rules refuse it, or another game has claimed it) is left for the editor, which makes the
    /// repository when that folder is set again, and does not fail the call. No history is copied or moved, and the only things
    /// deleted are hooks. Games and profiles stay listed, and a profile the folder holds no history for starts it again with its next
    /// save. Check again afterwards.
    /// </summary>
    /// <remarks>
    /// Failures, in the order the checks run; the settings stay as they were for every one of them:
    /// <c>error.validation.invalidPayload</c> when the folder is not an absolute path SavePoint can read (an unknown token, a relative
    /// path, a character that cannot be in it); <c>error.storage.stillReachable</c> when the saved folder can be reached;
    /// <c>error.storage.noHistoryHere</c> when the folder is missing or holds no history for the games; <c>error.storage.unreachable</c>
    /// when the folder cannot be used as a storage folder at all; <c>error.storageRelocation.insideInstallFolder</c>,
    /// <c>error.gameService.pathOverlap</c> and <c>error.storageRelocation.moveUnfinished</c> for the three move-destination checks;
    /// <c>error.storage.folderHasLink</c> when a game's folder, a git directory, or the config, hooks folder or other entry inside one is
    /// a link, or does not resolve to a path inside the folder, in which case nothing in the folder is changed; <c>error.gitOperation.failed</c> when a git directory's config cannot be read, so it cannot be made safe; and
    /// <c>error.catalog.corrupt</c> when a catalog file the call reads or writes cannot be read. A cancelled call fails with
    /// <c>error.operation.cancelled</c> (D23).
    /// </remarks>
    /// <param name="folder">The folder to use as the storage folder.</param>
    /// <param name="cancellationToken">Cancelling fails the call with <c>error.operation.cancelled</c> (D23).</param>
    /// <returns>The storage folder now in use.</returns>
    Task<OperationResult<string>> UseStorageFolderAsync(string folder, CancellationToken cancellationToken = default);

    /// <summary>
    /// "Start with an empty folder": gives up on the storage folder that cannot be reached and starts SavePoint's history over in the
    /// platform's default storage folder, which is made when it is missing. Both startup choices are refused while the saved folder can
    /// be reached. Games and profiles stay listed, with no saved history: every listed game gets its repositories in the new folder, an
    /// empty one for each of its folders whose consent marker names the game, so a profile starts its history again with its next save.
    /// A folder that cannot take a repository is left for the editor, as for <see cref="UseStorageFolderAsync"/>, and does not fail the
    /// call. What is left in the old folder is not touched or used.
    /// </summary>
    /// <remarks>
    /// Failures: <c>error.storage.stillReachable</c> when the saved folder can be reached; <c>error.storage.defaultFolderHasHistory</c>
    /// when the default folder already holds history of its own, which an empty start would hide (use <see cref="UseStorageFolderAsync"/>
    /// to point at it instead); <c>error.storage.unreachable</c> when the default folder cannot be made; and <c>error.catalog.corrupt</c>
    /// when a catalog file the call reads or writes cannot be read. The settings stay as they were for every one of them. A cancelled
    /// call fails with <c>error.operation.cancelled</c> (D23).
    /// </remarks>
    /// <param name="cancellationToken">Cancelling fails the call with <c>error.operation.cancelled</c> (D23).</param>
    /// <returns>The storage folder now in use.</returns>
    Task<OperationResult<string>> StartWithEmptyStorageAsync(CancellationToken cancellationToken = default);
}
