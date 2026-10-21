using Savepoint.Core.Catalog;

namespace Savepoint.Core.Api;

public interface IGamesApi
{
    // Each template's ToDraft() gives a definition with a fresh id; nothing is saved until AddAsync.
    IReadOnlyList<GameTemplate> Templates { get; }

    Task<OperationResult<IReadOnlyList<GameDefinition>>> ListAsync(CancellationToken cancellationToken = default);

    // Validates, checks path safety, consent, and overlap with other games, then saves.
    Task<OperationResult<AddGameOutcome>> AddAsync(GameDefinition definition);

    Task<OperationResult<GameDefinition>> ResolveOrphanAsync(GameDefinition definition, OrphanResolution resolution);

    // Also how a moved folder is re-pointed; consent is confirmed again for a new path.
    Task<OperationResult<GameDefinition>> UpdateAsync(GameDefinition definition);

    // Removes SavePoint's data for the game; refused while the game is running (D13.5).
    Task<OperationResult<Unit>> DeleteAsync(string gameId);

    Task<OperationResult<IReadOnlyList<FolderStatus>>> GetFolderStatusAsync(string gameId, CancellationToken cancellationToken = default);

    Task<OperationResult<GameStorageUsage>> GetStorageUsageAsync(string gameId, CancellationToken cancellationToken = default);

    Task<OperationResult<CapturePreview>> GetCapturePreviewAsync(string gameId, CancellationToken cancellationToken = default);

    Task<OperationResult<JobStarted>> OptimizeStorageAsync(string gameId);

    // Expands path tokens for the editor's "Resolves to ..." hint.
    Task<OperationResult<string>> ResolvePathAsync(string rawPath, CancellationToken cancellationToken = default);

    // Paths relative to the install folder, for the editor's executable suggestions.
    Task<OperationResult<IReadOnlyList<string>>> ListExecutablesAsync(string installPath, CancellationToken cancellationToken = default);

    // Reads and validates a definition file; never saves it.
    Task<OperationResult<GameDefinition>> ImportDefinitionAsync(string filePath, CancellationToken cancellationToken = default);

    Task<OperationResult<Unit>> ExportDefinitionAsync(string gameId, string filePath, CancellationToken cancellationToken = default);
}
