using Savepoint.Core.Catalog;

namespace Savepoint.Core.Api;

/// <summary>App settings and everything about SavePoint's own storage: size, optimization, moving it, and clearing it.</summary>
/// <remarks>See <see cref="OperationResult{T}"/> for the rules every member follows.</remarks>
public interface ISettingsApi
{
    /// <summary>Reads the settings, or the first-run defaults (<see cref="AppSettings.Defaults"/>) when none were saved yet.</summary>
    /// <param name="cancellationToken">Cancelling fails the call with <c>error.operation.cancelled</c> (D23).</param>
    Task<OperationResult<AppSettings>> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates and saves the settings. It keeps the stored <see cref="AppSettings.StorageRoot"/> and
    /// <see cref="AppSettings.LastOptimizedAt"/> whatever the argument holds: storage moves only through
    /// <see cref="RelocateStorageAsync"/> and the service owns the date (D23). Fails with <c>error.validation.invalidPayload</c>.
    /// </summary>
    /// <param name="settings">The settings to save.</param>
    /// <returns>The settings as stored.</returns>
    Task<OperationResult<AppSettings>> SaveAsync(AppSettings settings);

    /// <summary>Reads how much storage SavePoint uses in all and per game, how full the storage volume is, and when it was last optimized.</summary>
    /// <param name="cancellationToken">Cancelling fails the call with <c>error.operation.cancelled</c> (D23).</param>
    Task<OperationResult<StorageSummary>> GetStorageSummaryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Settings' "Run now" for Free up space: starts one <see cref="JobKind.Compact"/> job across every game, so its snapshot has no game
    /// id. For each game it first deletes the game's recently removed snapshots for good (see <see cref="IHistoryApi.GetRecentlyRemovedAsync"/>),
    /// then optimizes the storage and reclaims space nothing reaches any more. Answers at once; the result is an <see cref="OptimizeResult"/>.
    /// </summary>
    Task<OperationResult<JobStarted>> OptimizeAllStorageAsync();

    /// <summary>
    /// Counts what <see cref="OptimizeAllStorageAsync"/> would delete for good that no profile holds, across every game, so a
    /// confirmation can say so. Reads only the git directories.
    /// </summary>
    /// <param name="cancellationToken">Cancelling fails the call with <c>error.operation.cancelled</c> (D23).</param>
    Task<OperationResult<FreeUpSpacePreview>> GetFreeUpSpacePreviewAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sizes a move of the storage folder without doing it. A move that cannot go ahead is still a successful answer, with the
    /// reason in <see cref="StorageRelocationPreview.BlockedBy"/>: a folder that is not empty
    /// (<c>error.storageRelocation.destinationNotEmpty</c>), one that holds the partial copy of an earlier move that never finished
    /// (<c>error.storageRelocation.moveUnfinished</c>), an unsafe one, too little space (<c>error.diskSpace.low</c>), or a
    /// running game (<c>error.gameService.gameRunning</c>), one inside the folder SavePoint is installed in, which an update or an
    /// uninstall deletes (<c>error.storageRelocation.insideInstallFolder</c>), or something in the current storage that cannot be
    /// read or carried over (<c>error.storageRelocation.sourceUnreadable</c>, the entry in its <c>path</c> argument).
    /// </summary>
    /// <param name="newRoot">The folder the storage would move to.</param>
    /// <param name="cancellationToken">Cancelling fails the call with <c>error.operation.cancelled</c> (D23).</param>
    Task<OperationResult<StorageRelocationPreview>> GetRelocationPreviewAsync(string newRoot, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a <see cref="JobKind.RelocateStorage"/> job that copies the storage to a new folder. The settings point at the new
    /// folder only once the job has succeeded; a failed or cancelled move leaves them alone and keeps the old copies. After the
    /// switch the job removes SavePoint's own copies from the previous folder (only git directories it recognizes as its own, and
    /// never the folder itself); a cancel no longer stops that removal, and a copy it cannot remove does not fail the move but ends
    /// the job <see cref="JobStatus.Partial"/> with the copy listed in <see cref="StorageRelocationResult.NotRemoved"/>. While it
    /// copies, the destination holds a marker file that the move removes when it ends, so a destination left by a move that was
    /// cut short is refused later. Answers at once, or fails with
    /// <c>error.gameService.gameRunning</c> when a game has an open session (D13.5), or with the same errors as the preview lists
    /// for a move that cannot go ahead. The result is a <see cref="StorageRelocationResult"/>.
    /// </summary>
    /// <param name="newRoot">The folder to move the storage to.</param>
    Task<OperationResult<JobStarted>> RelocateStorageAsync(string newRoot);

    /// <summary>
    /// Lists what clearing all data would remove, with the reason in <see cref="ClearAllDataPreview.BlockedBy"/> when it cannot go
    /// ahead.
    /// </summary>
    /// <param name="cancellationToken">Cancelling fails the call with <c>error.operation.cancelled</c> (D23).</param>
    Task<OperationResult<ClearAllDataPreview>> GetClearAllDataPreviewAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a <see cref="JobKind.ClearData"/> job that removes SavePoint's storage, catalog, and consent markers; it never
    /// deletes or rolls back files in a game's install or save folders (D8). Not cancellable, because stopping halfway would leave
    /// some games cleared and others not. Answers at once, or fails with <c>error.gameService.gameRunning</c> when a game has an
    /// open session (D13.5). The result is a <see cref="ClearAllDataResult"/>.
    /// </summary>
    Task<OperationResult<JobStarted>> ClearAllDataAsync();
}
