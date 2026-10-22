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
    /// folder unreachable. Both startup choices are refused while the saved folder can be reached, and that is checked again once the
    /// call holds its place in the queue for the storage, so a choice that waited never replaces a root that came back meanwhile. The
    /// chosen folder is judged before anything changes: it has to exist and hold SavePoint's own history for at least one of the games
    /// in the catalog (for any game folder SavePoint made, when the catalog lists none); it has to pass the checks a storage move makes
    /// on its destination, so it is not inside the folder the installer owns, does not overlap one of a game's own folders, and is not
    /// what an unfinished move left behind; and it must hold no link where SavePoint reads or writes, so a folder someone else prepared
    /// cannot lead a write or a delete to files outside it. Each git directory in it is then made safe to use: its config is rewritten
    /// to the settings SavePoint writes and its hooks folder is emptied, so nothing the folder carries can run a program. Last, every
    /// listed game gets its repositories in the folder, an empty one for each of its folders that has none there (or whose repository
    /// an earlier start left half made) and whose consent marker names the game. A folder that cannot be confirmed (it is gone, the path
    /// rules refuse it, or another game has claimed it) is logged and left: its first save makes the repository once the folder
    /// confirms the game, and the editor makes it when the folder is set again. Such a folder does not fail the call. No history is
    /// copied or moved, and the only things deleted are hooks. Games and profiles stay listed, and a profile the folder holds no
    /// history for starts it again with its next save. Check again afterwards.
    /// </summary>
    /// <remarks>
    /// Failures, in the order the checks run; the settings stay as they were for every one of them:
    /// <list type="bullet">
    /// <item><c>error.validation.invalidPayload</c>: the folder is not an absolute path SavePoint can read (an unknown token, a relative path, a character that cannot be in it).</item>
    /// <item><c>error.storage.stillReachable</c>: the saved folder can be reached.</item>
    /// <item><c>error.storage.noHistoryHere</c>: the folder is missing or holds no history for the games.</item>
    /// <item><c>error.storage.unreachable</c>: the folder cannot be used as a storage folder at all.</item>
    /// <item><c>error.storageRelocation.insideInstallFolder</c>, <c>error.gameService.pathOverlap</c> and <c>error.storageRelocation.moveUnfinished</c>: the three move-destination checks.</item>
    /// <item><c>error.storage.folderHasLink</c>: a game's folder, a git directory, or the config, hooks folder or other entry inside one is a link, or does not resolve to a path inside the folder. Nothing in the folder is changed.</item>
    /// <item><c>error.gitOperation.failed</c>: a git directory's config cannot be read, so it cannot be made safe.</item>
    /// <item><c>error.storage.writeFailed</c>: a repository cannot be written because the drive is full or read-only. The call can be made again.</item>
    /// <item><c>error.catalog.corrupt</c>: a catalog file the call reads or writes cannot be read.</item>
    /// </list>
    /// A cancelled call fails with <c>error.operation.cancelled</c> (D23).
    /// </remarks>
    /// <param name="folder">The folder to use as the storage folder.</param>
    /// <param name="cancellationToken">Cancelling fails the call with <c>error.operation.cancelled</c> (D23).</param>
    /// <returns>The storage folder now in use.</returns>
    Task<OperationResult<string>> UseStorageFolderAsync(string folder, CancellationToken cancellationToken = default);

    /// <summary>
    /// "Start with an empty folder": gives up on the storage folder that cannot be reached and starts SavePoint's history over in the
    /// platform's default storage folder, which is made when it is missing. Both startup choices are refused while the saved folder can
    /// be reached, and that is checked again once the call holds its place in the queue for the storage. Games and profiles stay
    /// listed, with no saved history: every listed game gets its repositories in the new folder, an empty one for each of its folders
    /// whose consent marker names the game, so a profile starts its history again with its next save. A folder that cannot be confirmed
    /// is handled as for <see cref="UseStorageFolderAsync"/>, and does not fail the call. What is left in the old folder is not touched
    /// or used.
    /// </summary>
    /// <remarks>
    /// Failures; the settings stay as they were for every one of them:
    /// <list type="bullet">
    /// <item><c>error.storage.stillReachable</c>: the saved folder can be reached.</item>
    /// <item><c>error.storage.defaultFolderHasHistory</c>: the default folder already holds history of its own, which an empty start would hide (use <see cref="UseStorageFolderAsync"/> to point at it instead). Empty repositories, and what an earlier start left half made, are not history.</item>
    /// <item><c>error.storage.unreachable</c>: the default folder cannot be made.</item>
    /// <item><c>error.storage.writeFailed</c>: a repository cannot be written because the drive is full or read-only. The call can be made again, and finishes what the failed one left.</item>
    /// <item><c>error.catalog.corrupt</c>: a catalog file the call reads or writes cannot be read.</item>
    /// </list>
    /// A cancelled call fails with <c>error.operation.cancelled</c> (D23).
    /// </remarks>
    /// <param name="cancellationToken">Cancelling fails the call with <c>error.operation.cancelled</c> (D23).</param>
    /// <returns>The storage folder now in use.</returns>
    Task<OperationResult<string>> StartWithEmptyStorageAsync(CancellationToken cancellationToken = default);
}
