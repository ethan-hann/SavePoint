using Savepoint.Core.Catalog;

namespace Savepoint.Core.Api;

/// <summary>The game library: definitions, their folders, storage, and import and export of definition files.</summary>
/// <remarks>See <see cref="OperationResult{T}"/> for the rules every member follows.</remarks>
public interface IGamesApi
{
    /// <summary>
    /// The built-in game templates. Each template's <see cref="GameTemplate.ToDraft"/> gives a definition with a fresh id; nothing is
    /// saved until <see cref="AddAsync"/>. Reads memory and cannot fail.
    /// </summary>
    IReadOnlyList<GameTemplate> Templates { get; }

    /// <summary>Lists every game in catalog order. Fails with <c>error.catalog.corrupt</c> when the games file is unreadable.</summary>
    /// <param name="cancellationToken">Cancelling fails the call with <c>error.operation.cancelled</c> (D23).</param>
    Task<OperationResult<IReadOnlyList<GameDefinition>>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a definition, checks path safety, consent, and overlap with other games, then saves it. Answers
    /// <see cref="AddGameOutcome.Added"/>, or <see cref="AddGameOutcome.HistoryFound"/> when the folders hold history from a removed
    /// game, in which case nothing is saved and <see cref="ResolveOrphanAsync"/> decides. Fails with
    /// <c>error.validation.invalidPayload</c> (every field problem at once), <c>error.pathSafety.tier1Blocked</c> or
    /// <c>error.pathSafety.tier2Blocked</c>, <c>error.consentMarker.conflict</c>, <c>error.gameService.pathOverlap</c>, or
    /// <c>error.gameService.alreadyExists</c> when the id is taken.
    /// </summary>
    /// <param name="definition">The game to add, with an id the caller made (see <see cref="CatalogIds.NewGameId"/>).</param>
    Task<OperationResult<AddGameOutcome>> AddAsync(GameDefinition definition);

    /// <summary>
    /// Settles the question <see cref="AddGameOutcome.HistoryFound"/> raised: reconnects the old history or starts fresh, and adds
    /// the game either way. Fails with the ids <see cref="AddAsync"/> can, and with <c>error.gameService.noHistoryToReconnect</c> when
    /// a reconnect finds no saved history under the old id at the game's folders.
    /// </summary>
    /// <param name="definition">The game being added, as it was given to <see cref="AddAsync"/>.</param>
    /// <param name="resolution">The user's answer.</param>
    /// <returns>The game as stored, with the old id after a reconnect.</returns>
    Task<OperationResult<GameDefinition>> ResolveOrphanAsync(GameDefinition definition, OrphanResolution resolution);

    /// <summary>
    /// Saves changes to a game. Also how a moved folder is re-pointed; consent is confirmed again for a new path. Fails with the
    /// ids <see cref="AddAsync"/> can. When a folder that has saved points is pointed somewhere else, the new folder is compared
    /// with that folder's newest point (D31): if fewer than half of the recorded files are present at the same relative paths the
    /// call fails with <c>error.gameService.folderMismatch</c> (args <c>role</c>, <c>path</c>, <c>presentCount</c>,
    /// <c>recordedCount</c>) and saves nothing, so the caller can ask and then repeat the call with <paramref name="confirmFolderChange"/>.
    /// A folder cannot be changed while the game has an open session, including one that waits for the player's choice after play:
    /// the call fails with <c>error.gameService.gameRunning</c>. Other edits are allowed.
    /// </summary>
    /// <param name="definition">The game with its changes; its id picks the game.</param>
    /// <param name="confirmFolderChange">True when the user agreed to a folder that holds few of the recorded files.</param>
    /// <returns>The game as stored.</returns>
    Task<OperationResult<GameDefinition>> UpdateAsync(GameDefinition definition, bool confirmFolderChange = false);

    /// <summary>
    /// Removes SavePoint's data for the game. Fails with <c>error.gameService.gameRunning</c> while the game has an open session
    /// (D13.5), and with <c>error.storage.unreachable</c> while the storage folder is not there, so the history behind it is
    /// never forgotten by mistake (D31).
    /// </summary>
    /// <param name="gameId">The game to remove.</param>
    Task<OperationResult<Unit>> DeleteAsync(string gameId);

    /// <summary>
    /// Reports where each of the game's folders is and whether the user confirmed it: one entry per configured folder, install
    /// first, then the saves folder when the game has one.
    /// </summary>
    /// <param name="gameId">The game.</param>
    /// <param name="cancellationToken">Cancelling fails the call with <c>error.operation.cancelled</c> (D23).</param>
    Task<OperationResult<IReadOnlyList<FolderStatus>>> GetFolderStatusAsync(string gameId, CancellationToken cancellationToken = default);

    /// <summary>Reads how much of SavePoint's storage the game takes and how full the storage volume is.</summary>
    /// <param name="gameId">The game.</param>
    /// <param name="cancellationToken">Cancelling fails the call with <c>error.operation.cancelled</c> (D23).</param>
    Task<OperationResult<GameStorageUsage>> GetStorageUsageAsync(string gameId, CancellationToken cancellationToken = default);

    /// <summary>Lists what a capture would save now, per folder, without writing anything.</summary>
    /// <param name="gameId">The game.</param>
    /// <param name="cancellationToken">Cancelling fails the call with <c>error.operation.cancelled</c> (D23).</param>
    Task<OperationResult<CapturePreview>> GetCapturePreviewAsync(string gameId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a <see cref="JobKind.Compact"/> job that frees up the game's space: it first deletes the recently removed snapshots
    /// the player agreed to lose (see <see cref="IHistoryApi.GetRecentlyRemovedAsync"/>), then optimizes the storage and reclaims space
    /// nothing reaches any more. Only the records in <paramref name="confirmed"/> are deleted; a snapshot removed after the
    /// preview that made it stays, and a run without it deletes none. Nothing else deletes a recently removed snapshot. Answers at once;
    /// the result is an <see cref="OptimizeResult"/>.
    /// </summary>
    /// <param name="gameId">The game.</param>
    /// <param name="confirmed">The token of <see cref="GetFreeUpSpacePreviewAsync"/>, as the player agreed to it; null deletes no recently removed snapshot.</param>
    Task<OperationResult<JobStarted>> OptimizeStorageAsync(string gameId, FreeUpSpaceToken? confirmed = null);

    /// <summary>
    /// Counts what <see cref="OptimizeStorageAsync"/> would delete for good that no profile holds, so a confirmation can say so, and
    /// names the records behind the count so the run deletes exactly those. Reads only the git directories.
    /// </summary>
    /// <param name="gameId">The game.</param>
    /// <param name="cancellationToken">Cancelling fails the call with <c>error.operation.cancelled</c>.</param>
    Task<OperationResult<FreeUpSpacePreview>> GetFreeUpSpacePreviewAsync(string gameId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Expands a leading path token such as <c>{documents}</c> for the editor's "Resolves to ..." hint. Fails with
    /// <c>error.validation.invalidPayload</c> whose issue is <c>validation.path.unknownToken</c>, <c>validation.path.notAbsolute</c>,
    /// or <c>validation.path.invalidCharacter</c>.
    /// </summary>
    /// <param name="rawPath">The path as the user wrote it.</param>
    /// <param name="cancellationToken">Cancelling fails the call with <c>error.operation.cancelled</c> (D23).</param>
    /// <returns>The absolute path on this machine.</returns>
    Task<OperationResult<string>> ResolvePathAsync(string rawPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Suggests files in an install folder that could launch the game, for the editor. The scan is bounded: at most 50 paths, most
    /// likely game executable first. A folder that is missing gives an empty list, not an error.
    /// </summary>
    /// <param name="installPath">The install folder as the user wrote it; a leading path token is expanded before the scan.</param>
    /// <param name="cancellationToken">Cancelling fails the call with <c>error.operation.cancelled</c> (D23).</param>
    /// <returns>Paths relative to the install folder.</returns>
    Task<OperationResult<IReadOnlyList<string>>> ListExecutablesAsync(string installPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Suggests the process name for a game started through a link: the program in the install folder that most likely is the game
    /// itself, ranked above launchers, anti-cheat bootstraps, crash reporters, and tools. Reads the folder only.
    /// </summary>
    /// <param name="installPath">The install folder as the user wrote it; a leading path token is expanded before the scan.</param>
    /// <param name="gameName">The game's name as the user wrote it, used as a hint; may be blank.</param>
    /// <param name="cancellationToken">Cancelling fails the call with <c>error.operation.cancelled</c> (D23).</param>
    /// <returns>The file name with its extension, or an empty string when the folder is missing or nothing in it looks like a game.</returns>
    Task<OperationResult<string>> SuggestProcessNameAsync(string installPath, string gameName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads and validates a definition file; never saves it. The definition keeps the file's id, which may belong to a game already
    /// in the library, so the caller gives the new game an id of its own before adding it.
    /// </summary>
    /// <param name="filePath">The definition file to read.</param>
    /// <param name="cancellationToken">Cancelling fails the call with <c>error.operation.cancelled</c> (D23).</param>
    /// <returns>The definition in the file. Fails with <c>error.validation.invalidPayload</c> when the file is not a valid definition.</returns>
    Task<OperationResult<GameDefinition>> ImportDefinitionAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>Writes the game's saved definition to a file, replacing any file already there. Unsaved edits are not included.</summary>
    /// <param name="gameId">The game to export.</param>
    /// <param name="filePath">The file to write.</param>
    /// <param name="cancellationToken">Cancelling fails the call with <c>error.operation.cancelled</c> (D23).</param>
    Task<OperationResult<Unit>> ExportDefinitionAsync(string gameId, string filePath, CancellationToken cancellationToken = default);
}
