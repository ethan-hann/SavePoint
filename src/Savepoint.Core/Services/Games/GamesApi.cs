using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;

namespace Savepoint.Core.Services.Games;

/// <summary>The real <see cref="IGamesApi"/>: defines, changes, inspects and deletes games over the catalog and the git engine.</summary>
internal sealed partial class GamesApi : IGamesApi
{
    private readonly BackendServices _services;

    public GamesApi(BackendServices services)
    {
        _services = services;
    }

    public IReadOnlyList<GameTemplate> Templates => GameTemplates.All;

    public Task<OperationResult<IReadOnlyList<GameDefinition>>> ListAsync(CancellationToken cancellationToken = default) =>
        ApiGuard.NotBuilt<IReadOnlyList<GameDefinition>>();

    public Task<OperationResult<AddGameOutcome>> AddAsync(GameDefinition definition) => ApiGuard.NotBuilt<AddGameOutcome>();

    public Task<OperationResult<GameDefinition>> ResolveOrphanAsync(GameDefinition definition, OrphanResolution resolution) =>
        ApiGuard.NotBuilt<GameDefinition>();

    public Task<OperationResult<GameDefinition>> UpdateAsync(GameDefinition definition) => ApiGuard.NotBuilt<GameDefinition>();

    public Task<OperationResult<Unit>> DeleteAsync(string gameId) => ApiGuard.NotBuilt<Unit>();

    public Task<OperationResult<IReadOnlyList<FolderStatus>>> GetFolderStatusAsync(string gameId, CancellationToken cancellationToken = default) =>
        ApiGuard.NotBuilt<IReadOnlyList<FolderStatus>>();

    public Task<OperationResult<GameStorageUsage>> GetStorageUsageAsync(string gameId, CancellationToken cancellationToken = default) =>
        ApiGuard.NotBuilt<GameStorageUsage>();

    public Task<OperationResult<CapturePreview>> GetCapturePreviewAsync(string gameId, CancellationToken cancellationToken = default) =>
        ApiGuard.NotBuilt<CapturePreview>();

    public Task<OperationResult<JobStarted>> OptimizeStorageAsync(string gameId) => ApiGuard.NotBuilt<JobStarted>();

    public Task<OperationResult<string>> ResolvePathAsync(string rawPath, CancellationToken cancellationToken = default) =>
        ApiGuard.NotBuilt<string>();

    public Task<OperationResult<IReadOnlyList<string>>> ListExecutablesAsync(string installPath, CancellationToken cancellationToken = default) =>
        ApiGuard.NotBuilt<IReadOnlyList<string>>();

    public Task<OperationResult<GameDefinition>> ImportDefinitionAsync(string filePath, CancellationToken cancellationToken = default) =>
        ApiGuard.NotBuilt<GameDefinition>();

    public Task<OperationResult<Unit>> ExportDefinitionAsync(string gameId, string filePath, CancellationToken cancellationToken = default) =>
        ApiGuard.NotBuilt<Unit>();
}
