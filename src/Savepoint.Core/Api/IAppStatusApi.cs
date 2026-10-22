namespace Savepoint.Core.Api;

/// <summary>The startup checks and the recovery of corrupt catalog files (D18).</summary>
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
    /// folder unreachable. It is a settings change and nothing is copied, moved or deleted. The folder has to exist and hold
    /// SavePoint's own history for at least one of the games in the catalog (for any game folder SavePoint made, when the catalog
    /// lists none). Fails with <c>error.storage.noHistoryHere</c> when it does not, and with <c>error.storage.unreachable</c> for a
    /// path that cannot be a storage folder; the settings stay as they were then. Check again afterwards.
    /// </summary>
    /// <param name="folder">The folder to use as the storage folder.</param>
    /// <param name="cancellationToken">Cancelling fails the call with <c>error.operation.cancelled</c> (D23).</param>
    /// <returns>The storage folder now in use.</returns>
    Task<OperationResult<string>> UseStorageFolderAsync(string folder, CancellationToken cancellationToken = default);

    /// <summary>
    /// "Start with an empty folder": gives up on the storage folder that cannot be reached and starts SavePoint's history over in the
    /// platform's default storage folder, which is made when it is missing. Games and profiles stay listed, with no saved history.
    /// What is left in the old folder is not touched or used. Fails with <c>error.storageRelocation.destinationNotEmpty</c> when the
    /// default folder already holds history of its own (use <see cref="UseStorageFolderAsync"/> to point at it instead), and with
    /// <c>error.storage.unreachable</c> when it cannot be made. Check again afterwards.
    /// </summary>
    /// <param name="cancellationToken">Cancelling fails the call with <c>error.operation.cancelled</c> (D23).</param>
    /// <returns>The storage folder now in use.</returns>
    Task<OperationResult<string>> StartWithEmptyStorageAsync(CancellationToken cancellationToken = default);
}
